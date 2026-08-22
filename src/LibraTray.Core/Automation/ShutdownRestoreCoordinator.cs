using System.Security.Cryptography;
using System.Text;
using LibraTray.Core.Configuration;
using LibraTray.Core.Devices.LibraPro;

namespace LibraTray.Core.Automation;

public enum ShutdownRestoreOutcome
{
    Ignored,
    TicketSaved,
    LightsTurnedOn,
    StateAlreadyCurrent,
    StateRestored,
    SkippedDifferentDevice,
    SkippedExpired,
    SkippedNewerState,
    SkippedManualControl,
    Failed,
}

public sealed class ShutdownRestoreStatusEventArgs(
    ShutdownRestoreOutcome outcome,
    Exception? exception = null) : EventArgs
{
    public ShutdownRestoreOutcome Outcome { get; } = outcome;

    public Exception? Exception { get; } = exception;
}

public sealed class ShutdownRestoreCoordinator : IDisposable
{
    private static readonly TimeSpan MaximumTicketAge = TimeSpan.FromDays(7);

    private readonly IShutdownRestoreTicketStore _store;
    private readonly Func<string?> _readDeviceId;
    private readonly Func<LibraProState?> _readCurrentState;
    private readonly Func<
        LibraProTargetState,
        CancellationToken,
        Task<LibraProState>> _applyTargetState;
    private readonly Func<
        bool,
        bool,
        CancellationToken,
        Task<LibraProState>> _applyPowerState;
    private readonly Func<CancellationToken, Task<LibraProState?>> _refreshState;
    private readonly Func<DateTimeOffset> _getUtcNow;
    private readonly Func<LibraProTargetState?> _readCurrentTargetState;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private WindowsAutomationSettings _settings;
    private long _manualControlVersion;
    private bool _disposed;

    public ShutdownRestoreCoordinator(
        WindowsAutomationSettings settings,
        IShutdownRestoreTicketStore store,
        Func<string?> readDeviceId,
        Func<LibraProState?> readCurrentState,
        Func<
            LibraProTargetState,
            CancellationToken,
            Task<LibraProState>> applyTargetState,
        Func<CancellationToken, Task<LibraProState?>> refreshState,
        Func<DateTimeOffset>? getUtcNow = null,
        Func<bool, bool, CancellationToken, Task<LibraProState>>?
            applyPowerState = null,
        Func<LibraProTargetState?>? readCurrentTargetState = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(readDeviceId);
        ArgumentNullException.ThrowIfNull(readCurrentState);
        ArgumentNullException.ThrowIfNull(applyTargetState);
        ArgumentNullException.ThrowIfNull(refreshState);
        _settings = settings;
        _store = store;
        _readDeviceId = readDeviceId;
        _readCurrentState = readCurrentState;
        _applyTargetState = applyTargetState;
        _applyPowerState = applyPowerState ?? ((mainPower, backgroundPower, token) =>
        {
            LibraProState state = _readCurrentState()
                ?? throw new InvalidOperationException(
                    "No confirmed state is available for a power-only operation.");
            return _applyTargetState(
                new LibraProTargetState(
                    mainPower,
                    state.MainBrightness,
                    state.MainColorTemperature,
                    backgroundPower,
                    state.BackgroundBrightness,
                    state.BackgroundRgb),
                token);
        });
        _refreshState = refreshState;
        _getUtcNow = getUtcNow ?? (() => DateTimeOffset.UtcNow);
        _readCurrentTargetState = readCurrentTargetState
            ?? (() => _readCurrentState() is { } state
                ? ToTargetState(state)
                : null);
        if (!settings.ShutdownAndStartupEnabled)
        {
            TryClearTicket();
        }
    }

    public event EventHandler<ShutdownRestoreStatusEventArgs>? StatusChanged;

    public bool HasPendingRestore => _store.Load() is not null;

    public long ManualControlVersion =>
        Volatile.Read(ref _manualControlVersion);

    public void Configure(WindowsAutomationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _settings = settings;
        if (!settings.ShutdownAndStartupEnabled)
        {
            TryClearTicket();
        }
    }

    public void RecordManualControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _manualControlVersion);
    }

    public bool PrepareTicketForPotentialShutdown(
        AutomationRestoreSnapshot? lifecycleSnapshot = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_settings.ShutdownAndStartupEnabled)
        {
            return false;
        }

        string? deviceKey = CreateDeviceKey(_readDeviceId());
        LibraProState? current = _readCurrentState();
        if (deviceKey is null || current is null)
        {
            return false;
        }

        LibraProTargetState restoreTarget = lifecycleSnapshot?.Target
            ?? _readCurrentTargetState()
            ?? ToTargetState(current);
        if (!restoreTarget.MainPower && !restoreTarget.BackgroundPower)
        {
            TryClearTicket();
            return false;
        }

        _store.Save(
            new ShutdownRestoreTicket
            {
                DeviceKey = deviceKey,
                CreatedUtc = _getUtcNow(),
                RestoreTarget = restoreTarget,
            });
        return true;
    }

    public void CancelPotentialShutdown()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TryClearTicket();
    }

    public async Task PrepareForShutdownAsync(
        AutomationRestoreSnapshot? lifecycleSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_settings.ShutdownAndStartupEnabled)
            {
                Publish(ShutdownRestoreOutcome.Ignored);
                return;
            }

            string? deviceKey = CreateDeviceKey(_readDeviceId());
            LibraProState? current = _readCurrentState();
            if (deviceKey is null || current is null)
            {
                Publish(ShutdownRestoreOutcome.Failed);
                return;
            }

            LibraProTargetState restoreTarget = lifecycleSnapshot?.Target
                ?? _readCurrentTargetState()
                ?? ToTargetState(current);
            LibraProTargetState offTarget = WithPower(
                restoreTarget,
                enabled: false);

            if (!restoreTarget.MainPower && !restoreTarget.BackgroundPower)
            {
                TryClearTicket();
                Publish(ShutdownRestoreOutcome.StateAlreadyCurrent);
                return;
            }

            if (!PrepareTicketForPotentialShutdown(lifecycleSnapshot))
            {
                Publish(ShutdownRestoreOutcome.Failed);
                return;
            }

            if (!Matches(current, offTarget))
            {
                current = await _applyPowerState(
                        false,
                        false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!Matches(current, offTarget))
            {
                Publish(ShutdownRestoreOutcome.Failed);
                return;
            }

            Publish(ShutdownRestoreOutcome.TicketSaved);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(ShutdownRestoreOutcome.Failed);
        }
        catch (Exception exception)
        {
            Publish(ShutdownRestoreOutcome.Failed, exception);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task TryRestoreAfterStartupAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ShutdownRestoreTicket? ticket = _store.Load();
            if (!_settings.ShutdownAndStartupEnabled || ticket is null)
            {
                Publish(ShutdownRestoreOutcome.Ignored);
                return;
            }

            if (_getUtcNow() - ticket.CreatedUtc > MaximumTicketAge
                || ticket.CreatedUtc > _getUtcNow().AddMinutes(5))
            {
                _store.Clear();
                Publish(ShutdownRestoreOutcome.SkippedExpired);
                return;
            }

            string? deviceKey = CreateDeviceKey(_readDeviceId());
            if (deviceKey is null
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(deviceKey),
                    Encoding.ASCII.GetBytes(ticket.DeviceKey)))
            {
                _store.Clear();
                Publish(ShutdownRestoreOutcome.SkippedDifferentDevice);
                return;
            }

            long manualVersion = Volatile.Read(ref _manualControlVersion);
            LibraProState? current = await _refreshState(cancellationToken)
                .ConfigureAwait(false);
            if (current is null || ticket.RestoreTarget is null)
            {
                Publish(ShutdownRestoreOutcome.Failed);
                return;
            }

            if (manualVersion != Volatile.Read(ref _manualControlVersion))
            {
                _store.Clear();
                Publish(ShutdownRestoreOutcome.SkippedManualControl);
                return;
            }

            if (Matches(current, ticket.RestoreTarget))
            {
                _store.Clear();
                Publish(ShutdownRestoreOutcome.StateAlreadyCurrent);
                return;
            }

            LibraProTargetState expectedOff = WithPower(
                ticket.RestoreTarget,
                enabled: false);
            if (!Matches(current, expectedOff))
            {
                _store.Clear();
                Publish(ShutdownRestoreOutcome.SkippedNewerState);
                return;
            }

            LibraProState restored = await _applyTargetState(
                    ticket.RestoreTarget,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!Matches(restored, ticket.RestoreTarget))
            {
                Publish(ShutdownRestoreOutcome.Failed);
                return;
            }

            _store.Clear();
            Publish(ShutdownRestoreOutcome.StateRestored);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(ShutdownRestoreOutcome.Failed);
        }
        catch (Exception exception)
        {
            Publish(ShutdownRestoreOutcome.Failed, exception);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ApplyStartupPowerPolicyAsync(
        long expectedManualControlVersion,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_settings.StartWithWindows
                || !_settings.TurnOnLightsAfterWindowsStartup)
            {
                Publish(ShutdownRestoreOutcome.Ignored);
                return;
            }

            if (expectedManualControlVersion
                != Volatile.Read(ref _manualControlVersion))
            {
                Publish(ShutdownRestoreOutcome.SkippedManualControl);
                return;
            }

            LibraProState? current = await _refreshState(cancellationToken)
                .ConfigureAwait(false);
            if (current is null)
            {
                Publish(ShutdownRestoreOutcome.Failed);
                return;
            }

            if (expectedManualControlVersion
                != Volatile.Read(ref _manualControlVersion))
            {
                Publish(ShutdownRestoreOutcome.SkippedManualControl);
                return;
            }

            if (current.MainPower && current.BackgroundPower)
            {
                Publish(ShutdownRestoreOutcome.StateAlreadyCurrent);
                return;
            }

            current = await _applyPowerState(
                    true,
                    true,
                    cancellationToken)
                .ConfigureAwait(false);
            Publish(current.MainPower && current.BackgroundPower
                ? ShutdownRestoreOutcome.LightsTurnedOn
                : ShutdownRestoreOutcome.Failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(ShutdownRestoreOutcome.Failed);
        }
        catch (Exception exception)
        {
            Publish(ShutdownRestoreOutcome.Failed, exception);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void TryClearTicket()
    {
        try
        {
            _store.Clear();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Publish(ShutdownRestoreOutcome.Failed, exception);
        }
    }

    private void Publish(
        ShutdownRestoreOutcome outcome,
        Exception? exception = null) =>
        StatusChanged?.Invoke(
            this,
            new ShutdownRestoreStatusEventArgs(outcome, exception));

    private static string? CreateDeviceKey(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        byte[] value = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                deviceId.Trim().ToUpperInvariant()));
        return Convert.ToHexString(value);
    }

    internal static LibraProTargetState ToTargetState(LibraProState state) =>
        new(
            state.MainPower,
            state.MainBrightness,
            state.MainColorTemperature,
            state.BackgroundPower,
            state.BackgroundBrightness,
            state.BackgroundRgb);

    internal static LibraProTargetState WithPower(
        LibraProTargetState state,
        bool enabled) =>
        new(
            enabled,
            state.MainBrightness,
            state.MainColorTemperature,
            enabled,
            state.BackgroundBrightness,
            state.BackgroundRgb,
            state.AmbientColorMode,
            state.SegmentRgb);

    internal static bool Matches(
        LibraProState state,
        LibraProTargetState target) =>
        state.MainPower == target.MainPower
        && state.MainBrightness == target.MainBrightness
        && state.MainColorTemperature == target.MainColorTemperature
        && state.BackgroundPower == target.BackgroundPower
        && state.BackgroundBrightness == target.BackgroundBrightness
        && (target.AmbientColorMode == AmbientColorMode.Segmented
            || state.BackgroundRgb == target.BackgroundRgb);
}
