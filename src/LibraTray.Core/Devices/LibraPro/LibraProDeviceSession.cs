using System.Net;
using System.Net.Sockets;
using LibraTray.Core.Identity;
using LibraTray.Core.Networking;
using LibraTray.Core.Protocol;

namespace LibraTray.Core.Devices.LibraPro;

public enum LibraProSessionStatus
{
    Disconnected,
    Discovering,
    Connecting,
    Connected,
    Retrying,
    Faulted,
}

public sealed class LibraProSessionStatusChangedEventArgs : EventArgs
{
    public LibraProSessionStatusChangedEventArgs(
        LibraProSessionStatus status,
        string message,
        Exception? exception = null,
        int retryAttempt = 0,
        int retryLimit = 0)
    {
        Status = status;
        Message = message;
        Exception = exception;
        RetryAttempt = retryAttempt;
        RetryLimit = retryLimit;
    }

    public LibraProSessionStatus Status { get; }

    public string Message { get; }

    public Exception? Exception { get; }

    public int RetryAttempt { get; }

    public int RetryLimit { get; }
}

public sealed class LibraProStateChangedEventArgs : EventArgs
{
    public LibraProStateChangedEventArgs(LibraProState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
    }

    public LibraProState State { get; }
}

public sealed record LibraProDeviceSessionOptions
{
    public TimeSpan MinimumCommandInterval { get; init; } =
        TimeSpan.FromMilliseconds(1_100);
}

/// <summary>
/// Owns one trusted Libra Pro discovery, TCP client, product adapter, and
/// notification reconciliation lifetime for a desktop host.
/// </summary>
public sealed class LibraProDeviceSession : IAsyncDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NotificationDebounce = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(900),
    ];

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly LibraProDeviceSessionOptions _options;

    private YeelightClient? _client;
    private LibraProAdapter? _adapter;
    private RateLimitedYeelightCommandTransport? _transport;
    private LibraProState? _currentState;
    private long _connectionEpoch;
    private int _commandInProgress;
    private int _notificationRefreshPending;
    private bool _disposed;

    public LibraProDeviceSession(
        LibraProDeviceSessionOptions? options = null)
    {
        _options = options ?? new LibraProDeviceSessionOptions();
        if (_options.MinimumCommandInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The minimum command interval cannot be negative.");
        }
    }

    public event EventHandler<LibraProSessionStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<LibraProStateChangedEventArgs>? StateChanged;

    public LibraProSessionStatus Status { get; private set; } =
        LibraProSessionStatus.Disconnected;

    public DeviceIdentity? Identity { get; private set; }

    public LibraProState? CurrentState => Volatile.Read(ref _currentState);

    public async Task DiscoverAndConnectAsync(
        YeelightDiscoveryOptions? discoveryOptions = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SetStatus(LibraProSessionStatus.Discovering, "Searching the local network.");
        IReadOnlyList<YeelightDiscoveredDevice> discovered =
            await YeelightDiscoveryClient
                .DiscoverAsync(discoveryOptions, cancellationToken)
                .ConfigureAwait(false);
        YeelightDiscoveredDevice[] matches = discovered
            .Where(device => ProductIdentityMapper.IsSupportedInternalModel(
                device.Response.Model))
            .ToArray();

        if (matches.Length == 0)
        {
            SetStatus(
                LibraProSessionStatus.Disconnected,
                "No trusted Yeelight Libra Pro was discovered.");
            throw new InvalidOperationException(
                "No discovery response with the exact lamp15 identity was found.");
        }

        if (matches.Length > 1)
        {
            SetStatus(
                LibraProSessionStatus.Disconnected,
                "Multiple Yeelight Libra Pro devices require explicit selection.");
            throw new InvalidOperationException(
                "Multiple exact lamp15 devices were discovered; automatic selection is refused.");
        }

        await ConnectAsync(matches[0], cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectAsync(
        YeelightDiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_client is not null)
            {
                throw new InvalidOperationException(
                    "The Libra Pro session is already connected.");
            }

            DeviceIdentity identity = ProductIdentityMapper.Resolve(
                device.Response.Model,
                device.Response.ReportedName);
            if (!identity.IsKnownProduct)
            {
                throw new ArgumentException(
                    "The session requires the exact lamp15 discovery identity.",
                    nameof(device));
            }

            string[] capabilities = ParseCapabilities(device.Response);
            if (!capabilities.Contains("get_prop", StringComparer.Ordinal))
            {
                throw new NotSupportedException(
                    "The device did not advertise get_prop.");
            }

            SetStatus(LibraProSessionStatus.Connecting, "Connecting to the device.");
            var client = new YeelightClient(CommandTimeout);
            LibraProAdapter? adapter = null;
            RateLimitedYeelightCommandTransport? transport = null;

            try
            {
                await client
                    .ConnectAsync(device.Response.ControlEndPoint, cancellationToken)
                    .ConfigureAwait(false);
                transport = new RateLimitedYeelightCommandTransport(
                    new YeelightCommandTransport(client),
                    _options.MinimumCommandInterval);
                adapter = new LibraProAdapter(
                    transport,
                    identity.InternalModel!,
                    capabilities);
                adapter.BeginConnectionEpoch(++_connectionEpoch);
                LibraProState state = await adapter
                    .QueryStateAsync(CommandTimeout, cancellationToken)
                    .ConfigureAwait(false);

                client.NotificationReceived += OnNotificationReceived;
                _client = client;
                _adapter = adapter;
                _transport = transport;
                Identity = identity;
                PublishState(state);
                SetStatus(LibraProSessionStatus.Connected, "Connected.");
            }
            catch
            {
                adapter?.Dispose();
                transport?.Dispose();
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            SetStatus(
                LibraProSessionStatus.Faulted,
                "The device connection failed.",
                exception);
            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<LibraProState> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
        CancellationToken operationToken = operationCancellation.Token;
        await _operationLock.WaitAsync(operationToken).ConfigureAwait(false);

        try
        {
            LibraProState state = await ExecuteRetriableAsync(
                    adapter => adapter.QueryStateAsync(
                        CommandTimeout,
                        operationToken),
                    "state synchronization",
                    "State reconciliation failed.",
                    operationToken)
                .ConfigureAwait(false);
            PublishState(state);
            ConfirmConnected();
            return state;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public Task<LibraProState> SetMainPowerAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (adapter, operationToken) => adapter.SetMainPowerAsync(
                enabled,
                CommandTimeout,
                operationToken),
            cancellationToken);

    public Task<LibraProState> SetBackgroundPowerAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        LibraProState? current = CurrentState;
        LibraProBackgroundSnapshot? snapshot = current is null
            ? null
            : new LibraProBackgroundSnapshot(
                current.BackgroundBrightness,
                current.BackgroundRgb);
        return ExecuteAsync(
            (adapter, operationToken) => adapter.SetBackgroundPowerAsync(
                enabled,
                snapshot,
                CommandTimeout,
                operationToken),
            cancellationToken);
    }

    public Task<LibraProState> SetMainBrightnessAsync(
        int brightness,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (adapter, operationToken) => adapter.SetMainBrightnessAsync(
                brightness,
                CommandTimeout,
                operationToken),
            cancellationToken);

    public Task<LibraProState> SetMainColorTemperatureAsync(
        int colorTemperature,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (adapter, operationToken) => adapter.SetMainColorTemperatureAsync(
                colorTemperature,
                CommandTimeout,
                operationToken),
            cancellationToken);

    public Task<LibraProState> SetBackgroundBrightnessAsync(
        int brightness,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (adapter, operationToken) => adapter.SetBackgroundBrightnessAsync(
                brightness,
                CommandTimeout,
                operationToken),
            cancellationToken);

    public Task<LibraProState> SetBackgroundColorTemperatureAsync(
        int colorTemperature,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (adapter, operationToken) => adapter.SetBackgroundColorTemperatureAsync(
                colorTemperature,
                CommandTimeout,
                operationToken),
            cancellationToken);

    public Task<LibraProState> SetBackgroundRgbAsync(
        int rgb,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (adapter, operationToken) => adapter.SetBackgroundRgbAsync(
                rgb,
                CommandTimeout,
                operationToken),
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        await _operationLock.WaitAsync().ConfigureAwait(false);
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);

        try
        {
            YeelightClient? client = _client;
            LibraProAdapter? adapter = _adapter;
            RateLimitedYeelightCommandTransport? transport = _transport;
            _client = null;
            _adapter = null;
            _transport = null;
            Identity = null;
            Volatile.Write(ref _currentState, null);

            if (client is not null)
            {
                client.NotificationReceived -= OnNotificationReceived;
            }

            adapter?.Dispose();
            transport?.Dispose();
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            SetStatus(LibraProSessionStatus.Disconnected, "Disconnected.");
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
            _operationLock.Release();
            _operationLock.Dispose();
            _lifetimeCancellation.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<LibraProState> ExecuteAsync(
        Func<
            LibraProAdapter,
            CancellationToken,
            Task<LibraProCommandResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
        CancellationToken operationToken = operationCancellation.Token;
        await _operationLock.WaitAsync(operationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _commandInProgress);

        try
        {
            LibraProCommandResult result = await ExecuteRetriableAsync(
                    adapter => operation(adapter, operationToken),
                    "device command",
                    "The device command failed.",
                    operationToken)
                .ConfigureAwait(false);
            PublishState(result.State);
            ConfirmConnected();
            return result.State;
        }
        finally
        {
            Interlocked.Decrement(ref _commandInProgress);
            _operationLock.Release();
        }
    }

    private void OnNotificationReceived(
        object? sender,
        YeelightNotificationEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Notification is not YeelightPropsNotification
            || Volatile.Read(ref _commandInProgress) > 0
            || Interlocked.Exchange(ref _notificationRefreshPending, 1) != 0)
        {
            return;
        }

        _ = RefreshAfterNotificationAsync();
    }

    private async Task RefreshAfterNotificationAsync()
    {
        try
        {
            await Task.Delay(
                    NotificationDebounce,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(false);
            await RefreshAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _ = exception;
        }
        finally
        {
            Interlocked.Exchange(ref _notificationRefreshPending, 0);
        }
    }

    private LibraProAdapter GetConnectedAdapter()
    {
        ThrowIfDisposed();
        return _adapter
            ?? throw new InvalidOperationException(
                "The Libra Pro session is not connected.");
    }

    private void PublishState(LibraProState state)
    {
        Volatile.Write(ref _currentState, state);
        StateChanged?.Invoke(this, new LibraProStateChangedEventArgs(state));
    }

    private void SetStatus(
        LibraProSessionStatus status,
        string message,
        Exception? exception = null,
        int retryAttempt = 0,
        int retryLimit = 0)
    {
        Status = status;
        StatusChanged?.Invoke(
            this,
            new LibraProSessionStatusChangedEventArgs(
                status,
                message,
                exception,
                retryAttempt,
                retryLimit));
    }

    private void ConfirmConnected()
    {
        if (Status != LibraProSessionStatus.Connected)
        {
            SetStatus(LibraProSessionStatus.Connected, "Connected.");
        }
    }

    private void ReportOperationFailure(string message, Exception exception)
    {
        bool transportConnected = _client?.IsConnected == true;
        SetStatus(
            transportConnected
                ? LibraProSessionStatus.Connected
                : LibraProSessionStatus.Faulted,
            transportConnected
                ? $"{message} The TCP connection remains available."
                : $"{message} The TCP connection is unavailable.",
            exception);
    }

    private async Task<TResult> ExecuteRetriableAsync<TResult>(
        Func<LibraProAdapter, Task<TResult>> operation,
        string operationName,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;

        for (int attempt = 0; ; attempt++)
        {
            if (attempt > 0)
            {
                SetStatus(
                    LibraProSessionStatus.Retrying,
                    $"Retrying {operationName}.",
                    lastFailure,
                    retryAttempt: attempt,
                    retryLimit: RetryDelays.Length);
                await Task.Delay(
                        RetryDelays[attempt - 1],
                        cancellationToken)
                    .ConfigureAwait(false);

                if (_client?.IsConnected != true)
                {
                    try
                    {
                        await ReconnectTransportAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is not OperationCanceledException)
                    {
                        lastFailure = exception;
                        if (attempt >= RetryDelays.Length
                            || !IsRetryable(exception))
                        {
                            ReportOperationFailure(
                                failureMessage,
                                exception);
                            throw;
                        }

                        continue;
                    }
                }
            }

            try
            {
                return await operation(GetConnectedAdapter())
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                lastFailure = exception;
                if (attempt >= RetryDelays.Length
                    || !IsRetryable(exception))
                {
                    ReportOperationFailure(
                        failureMessage,
                        exception);
                    throw;
                }
            }
        }
    }

    private async Task ReconnectTransportAsync(
        CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            YeelightClient client = _client
                ?? throw new YeelightDisconnectedException();
            LibraProAdapter adapter = _adapter
                ?? throw new YeelightDisconnectedException();
            if (!client.IsConnected)
            {
                await client.ReconnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                adapter.BeginConnectionEpoch(++_connectionEpoch);
                LibraProState state = await adapter
                    .QueryStateAsync(CommandTimeout, cancellationToken)
                    .ConfigureAwait(false);
                PublishState(state);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private static bool IsRetryable(Exception exception) =>
        exception is TimeoutException
            or YeelightDisconnectedException
            or SocketException
            or YeelightCommandException { ErrorCode: -1 }
            or LibraProStateVerificationException
            or YeelightProtocolException;

    private static string[] ParseCapabilities(
        YeelightDiscoveryResponse response)
    {
        if (!response.Headers.TryGetValue("support", out string? support))
        {
            return [];
        }

        return support.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
