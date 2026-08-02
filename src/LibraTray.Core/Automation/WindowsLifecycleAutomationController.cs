using LibraTray.Core.Configuration;
using LibraTray.Core.Devices.LibraPro;

namespace LibraTray.Core.Automation;

public enum WindowsLifecycleEventKind
{
    SessionEnding,
    SessionLocked,
    SessionUnlocked,
    DisplayOff,
    DisplayOn,
    DisplayDimmed,
}

public sealed record AutomationRestoreSnapshot(
    LibraProTargetState Target,
    bool PowerOffApplied);

public enum WindowsAutomationOutcome
{
    Ignored,
    StateCaptured,
    LightsTurnedOff,
    StateAlreadyCurrent,
    StateRestored,
    SkippedRecentManualControl,
    SkippedNewerState,
    Failed,
}

public sealed class WindowsAutomationStatusEventArgs(
    WindowsLifecycleEventKind trigger,
    WindowsAutomationOutcome outcome,
    Exception? exception = null) : EventArgs
{
    public WindowsLifecycleEventKind Trigger { get; } = trigger;

    public WindowsAutomationOutcome Outcome { get; } = outcome;

    public Exception? Exception { get; } = exception;
}

public sealed class WindowsLifecycleAutomationController : IDisposable
{
    private readonly Func<LibraProState?> _readCurrentState;
    private readonly Func<
        LibraProTargetState,
        CancellationToken,
        Task<LibraProState>> _applyTargetState;
    private readonly Func<CancellationToken, Task<LibraProState?>> _refreshState;
    private readonly Func<DateTimeOffset> _getUtcNow;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly HashSet<AutomationBlocker> _activeBlockers = [];

    private WindowsAutomationSettings _settings;
    private LibraProTargetState? _snapshot;
    private AutomationRestoreSnapshot? _pendingRestoreSnapshot;
    private bool _automationPowerOffApplied;
    private long _manualControlVersion;
    private long _snapshotManualControlVersion;
    private DateTimeOffset _lastManualControl = DateTimeOffset.MinValue;
    private bool _disposed;

    public WindowsLifecycleAutomationController(
        WindowsAutomationSettings settings,
        Func<LibraProState?> readCurrentState,
        Func<
            LibraProTargetState,
            CancellationToken,
            Task<LibraProState>> applyTargetState,
        Func<CancellationToken, Task<LibraProState?>> refreshState,
        Func<DateTimeOffset>? getUtcNow = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(readCurrentState);
        ArgumentNullException.ThrowIfNull(applyTargetState);
        ArgumentNullException.ThrowIfNull(refreshState);
        _settings = settings;
        _readCurrentState = readCurrentState;
        _applyTargetState = applyTargetState;
        _refreshState = refreshState;
        _getUtcNow = getUtcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public event EventHandler<WindowsAutomationStatusEventArgs>? StatusChanged;

    public AutomationRestoreSnapshot? PendingRestoreSnapshot =>
        Volatile.Read(ref _pendingRestoreSnapshot);

    public void Configure(WindowsAutomationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _settings = settings;
        if (!settings.RequiresLifecycleEvents)
        {
            ClearPendingRestore();
            _activeBlockers.Clear();
        }
    }

    public void RecordManualControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lastManualControl = _getUtcNow();
        Interlocked.Increment(ref _manualControlVersion);
    }

    public async Task HandleAsync(
        WindowsLifecycleEventKind trigger,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetEntryBlocker(trigger, out AutomationBlocker blocker))
            {
                await HandleEntryAsync(trigger, blocker, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (TryGetExitBlocker(trigger, out blocker))
            {
                await HandleExitAsync(trigger, blocker, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                Publish(trigger, WindowsAutomationOutcome.Ignored);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(trigger, WindowsAutomationOutcome.Failed, exception);
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

    private async Task HandleEntryAsync(
        WindowsLifecycleEventKind trigger,
        AutomationBlocker blocker,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(blocker) || !_activeBlockers.Add(blocker))
        {
            Publish(trigger, WindowsAutomationOutcome.Ignored);
            return;
        }

        if (IsRecentManualControl())
        {
            ClearPendingRestore();
            Publish(
                trigger,
                WindowsAutomationOutcome.SkippedRecentManualControl);
            return;
        }

        if (_snapshot is null && _readCurrentState() is { } current)
        {
            _snapshot = ToTargetState(current);
            _snapshotManualControlVersion =
                Volatile.Read(ref _manualControlVersion);
            PublishPendingRestoreSnapshot();
        }

        if (_snapshot is null)
        {
            Publish(trigger, WindowsAutomationOutcome.Ignored);
            return;
        }

        if (_automationPowerOffApplied)
        {
            Publish(trigger, WindowsAutomationOutcome.Ignored);
            return;
        }

        LibraProTargetState offTarget = WithPower(_snapshot, enabled: false);
        if (_readCurrentState() is { } state && Matches(state, offTarget))
        {
            _automationPowerOffApplied = true;
            PublishPendingRestoreSnapshot();
            Publish(trigger, WindowsAutomationOutcome.StateAlreadyCurrent);
            return;
        }

        _ = await _applyTargetState(offTarget, cancellationToken)
            .ConfigureAwait(false);
        _automationPowerOffApplied = true;
        PublishPendingRestoreSnapshot();
        Publish(trigger, WindowsAutomationOutcome.LightsTurnedOff);
    }

    private async Task HandleExitAsync(
        WindowsLifecycleEventKind trigger,
        AutomationBlocker blocker,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(blocker) || !_activeBlockers.Remove(blocker))
        {
            Publish(trigger, WindowsAutomationOutcome.Ignored);
            return;
        }

        if (_activeBlockers.Count > 0 || _snapshot is null)
        {
            Publish(trigger, WindowsAutomationOutcome.Ignored);
            return;
        }

        if (_snapshotManualControlVersion
            != Volatile.Read(ref _manualControlVersion))
        {
            ClearPendingRestore();
            Publish(
                trigger,
                WindowsAutomationOutcome.SkippedRecentManualControl);
            return;
        }

        LibraProState? refreshed = await _refreshState(cancellationToken)
            .ConfigureAwait(false);
        if (refreshed is null)
        {
            Publish(trigger, WindowsAutomationOutcome.Failed);
            return;
        }

        LibraProTargetState snapshot = _snapshot;
        LibraProTargetState expected = _automationPowerOffApplied
            ? WithPower(snapshot, enabled: false)
            : snapshot;
        if (Matches(refreshed, snapshot))
        {
            ClearPendingRestore();
            Publish(trigger, WindowsAutomationOutcome.StateAlreadyCurrent);
            return;
        }

        if (!Matches(refreshed, expected))
        {
            ClearPendingRestore();
            Publish(trigger, WindowsAutomationOutcome.SkippedNewerState);
            return;
        }

        _ = await _applyTargetState(snapshot, cancellationToken)
            .ConfigureAwait(false);
        ClearPendingRestore();
        Publish(trigger, WindowsAutomationOutcome.StateRestored);
    }

    private bool IsRecentManualControl() =>
        _getUtcNow() - _lastManualControl
        < TimeSpan.FromSeconds(_settings.ManualSuppressionSeconds);

    private bool IsEnabled(AutomationBlocker blocker) => blocker switch
    {
        AutomationBlocker.Lock => _settings.LockAndUnlockEnabled,
        AutomationBlocker.Display => _settings.DisplayPowerEnabled,
        _ => false,
    };

    private void ClearPendingRestore()
    {
        _snapshot = null;
        _automationPowerOffApplied = false;
        _snapshotManualControlVersion = 0;
        Volatile.Write(ref _pendingRestoreSnapshot, null);
    }

    private void PublishPendingRestoreSnapshot()
    {
        AutomationRestoreSnapshot? value = _snapshot is null
            ? null
            : new AutomationRestoreSnapshot(
                _snapshot,
                _automationPowerOffApplied);
        Volatile.Write(ref _pendingRestoreSnapshot, value);
    }

    private void Publish(
        WindowsLifecycleEventKind trigger,
        WindowsAutomationOutcome outcome,
        Exception? exception = null) =>
        StatusChanged?.Invoke(
            this,
            new WindowsAutomationStatusEventArgs(
                trigger,
                outcome,
                exception));

    private static bool TryGetEntryBlocker(
        WindowsLifecycleEventKind trigger,
        out AutomationBlocker blocker)
    {
        blocker = trigger switch
        {
            WindowsLifecycleEventKind.SessionLocked => AutomationBlocker.Lock,
            WindowsLifecycleEventKind.DisplayOff => AutomationBlocker.Display,
            _ => AutomationBlocker.None,
        };
        return blocker != AutomationBlocker.None;
    }

    private static bool TryGetExitBlocker(
        WindowsLifecycleEventKind trigger,
        out AutomationBlocker blocker)
    {
        blocker = trigger switch
        {
            WindowsLifecycleEventKind.SessionUnlocked => AutomationBlocker.Lock,
            WindowsLifecycleEventKind.DisplayOn => AutomationBlocker.Display,
            _ => AutomationBlocker.None,
        };
        return blocker != AutomationBlocker.None;
    }

    private static LibraProTargetState ToTargetState(LibraProState state) =>
        ShutdownRestoreCoordinator.ToTargetState(state);

    private static LibraProTargetState WithPower(
        LibraProTargetState state,
        bool enabled) =>
        ShutdownRestoreCoordinator.WithPower(state, enabled);

    private static bool Matches(
        LibraProState state,
        LibraProTargetState target) =>
        ShutdownRestoreCoordinator.Matches(state, target);

    private enum AutomationBlocker
    {
        None,
        Lock,
        Display,
    }
}
