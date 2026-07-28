using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using LibraTray.Core.Protocol;

namespace LibraTray.Core.Networking;

/// <summary>
/// Asynchronous, reusable TCP client for the generic Yeelight LAN JSON protocol.
/// It intentionally contains no device-specific commands or state assumptions.
/// </summary>
public sealed class YeelightClient : IAsyncDisposable
{
    private static readonly TimeSpan StandardRequestTimeout = TimeSpan.FromSeconds(5);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<long, TaskCompletionSource<YeelightResponse>> _pending = [];

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _connectionCancellation;
    private Task? _receiveLoop;
    private ConnectionTarget? _lastTarget;
    private int _connectionGeneration;
    private int _nextRequestId;
    private bool _disposed;

    public YeelightClient(TimeSpan? defaultRequestTimeout = null)
    {
        TimeSpan effectiveTimeout = defaultRequestTimeout ?? StandardRequestTimeout;
        ValidateTimeout(effectiveTimeout, nameof(defaultRequestTimeout));
        DefaultRequestTimeout = effectiveTimeout;
    }

    public event EventHandler<YeelightNotificationEventArgs>? NotificationReceived;

    /// <summary>
    /// Raised for a malformed, delimited JSON frame. Such a frame is skipped
    /// without failing unrelated in-flight requests.
    /// </summary>
    public event EventHandler<YeelightProtocolErrorEventArgs>? ProtocolErrorReceived;

    /// <summary>
    /// Raised when a syntactically valid response has no pending request ID.
    /// Late responses after a timeout are reported here and otherwise ignored.
    /// </summary>
    public event EventHandler<YeelightUnmatchedResponseEventArgs>? UnmatchedResponseReceived;

    /// <summary>
    /// Raised when a consumer callback throws. Callback failures are isolated
    /// and never terminate the transport receive loop.
    /// </summary>
    public event EventHandler<YeelightSubscriberErrorEventArgs>? SubscriberErrorReceived;

    public TimeSpan DefaultRequestTimeout { get; }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _stream is not null;
            }
        }
    }

    public Task ConnectAsync(
        IPEndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        return ConnectAsync(
            endPoint.Address.ToString(),
            endPoint.Port,
            cancellationToken);
    }

    public async Task ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                if (_stream is not null)
                {
                    throw new InvalidOperationException(
                        "The client is already connected. Disconnect it before connecting again.");
                }
            }

            var tcpClient = new TcpClient
            {
                NoDelay = true,
            };

            try
            {
                await tcpClient
                    .ConnectAsync(host, port, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }

            NetworkStream stream = tcpClient.GetStream();
            var connectionCancellation = new CancellationTokenSource();
            int generation;
            bool disposed;

            lock (_sync)
            {
                disposed = _disposed;
                if (disposed)
                {
                    generation = 0;
                }
                else
                {
                    _tcpClient = tcpClient;
                    _stream = stream;
                    _connectionCancellation = connectionCancellation;
                    _lastTarget = new ConnectionTarget(host, port);
                    generation = ++_connectionGeneration;
                }
            }

            if (disposed)
            {
                connectionCancellation.Dispose();
                tcpClient.Dispose();
                ThrowIfDisposed();
            }

            Task receiveLoop = ReceiveLoopAsync(
                stream,
                generation,
                connectionCancellation.Token);

            lock (_sync)
            {
                if (_connectionGeneration == generation && ReferenceEquals(_stream, stream))
                {
                    _receiveLoop = receiveLoop;
                }
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectionTarget? target;
        lock (_sync)
        {
            ThrowIfDisposed();
            target = _lastTarget;
        }

        if (target is null)
        {
            throw new InvalidOperationException(
                "The client has no previous endpoint to reconnect to.");
        }

        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await ConnectAsync(target.Host, target.Port, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            TcpClient? tcpClient;
            CancellationTokenSource? connectionCancellation;
            Task? receiveLoop;
            List<TaskCompletionSource<YeelightResponse>> pending;

            lock (_sync)
            {
                tcpClient = _tcpClient;
                connectionCancellation = _connectionCancellation;
                receiveLoop = _receiveLoop;

                _tcpClient = null;
                _stream = null;
                _connectionCancellation = null;
                _receiveLoop = null;
                _connectionGeneration++;

                pending = DrainPendingRequests();
            }

            connectionCancellation?.Cancel();
            tcpClient?.Dispose();

            var disconnectException = new YeelightDisconnectedException(
                "The Yeelight connection was closed by the client.");

            CompletePendingRequests(pending, disconnectException);

            if (receiveLoop is not null)
            {
                await receiveLoop
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            connectionCancellation?.Dispose();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<YeelightSuccessResponse> SendCommandAsync(
        string method,
        IEnumerable<object?>? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan effectiveTimeout = timeout ?? DefaultRequestTimeout;
        ValidateTimeout(effectiveTimeout, nameof(timeout));

        int requestId = GetNextRequestId();
        var request = new YeelightRequest(requestId, method, parameters);
        byte[] frame = YeelightRequestSerializer.Serialize(request);

        NetworkStream stream;
        int generation;
        var completion =
            new TaskCompletionSource<YeelightResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_sync)
        {
            ThrowIfDisposed();

            stream = _stream
                ?? throw new YeelightDisconnectedException();
            generation = _connectionGeneration;
            _pending.Add(requestId, completion);
        }

        using var timeoutCancellation = new CancellationTokenSource(effectiveTimeout);
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);

        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            await _writeLock.WaitAsync(operationToken).ConfigureAwait(false);

            try
            {
                lock (_sync)
                {
                    if (_connectionGeneration != generation
                        || !ReferenceEquals(_stream, stream))
                    {
                        throw new YeelightDisconnectedException();
                    }
                }

                await stream
                    .WriteAsync(frame, operationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            YeelightResponse response = await completion.Task
                .WaitAsync(operationToken)
                .ConfigureAwait(false);

            return response switch
            {
                YeelightSuccessResponse success => success,
                YeelightErrorResponse error => throw new YeelightCommandException(
                    error.Id,
                    error.Code,
                    error.Message),
                _ => throw new YeelightProtocolException(
                    "The matched response has an unsupported response type."),
            };
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Yeelight command {requestId} timed out after {effectiveTimeout}.");
        }
        catch (IOException exception)
        {
            HandleConnectionLost(generation, exception);
            throw;
        }
        catch (SocketException exception)
        {
            HandleConnectionLost(generation, exception);
            throw new YeelightDisconnectedException(
                "The Yeelight connection failed while sending a command.",
                exception);
        }
        finally
        {
            lock (_sync)
            {
                _pending.Remove(requestId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await DisconnectAsync().ConfigureAwait(false);

        _lifecycleLock.Dispose();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ReceiveLoopAsync(
        NetworkStream stream,
        int generation,
        CancellationToken cancellationToken)
    {
        var framer = new CrlfMessageFramer();
        byte[] readBuffer = new byte[8 * 1024];

        try
        {
            while (true)
            {
                int bytesRead = await stream
                    .ReadAsync(readBuffer, cancellationToken)
                    .ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    throw new YeelightDisconnectedException(
                        "The Yeelight device closed the TCP connection.");
                }

                IReadOnlyList<byte[]> frames = framer.Append(
                    readBuffer.AsSpan(0, bytesRead));

                foreach (byte[] frame in frames)
                {
                    if (frame.Length == 0)
                    {
                        continue;
                    }

                    YeelightMessage message;

                    try
                    {
                        message = YeelightMessageParser.Parse(frame);
                    }
                    catch (YeelightProtocolException exception)
                    {
                        InvokeSafely(
                            ProtocolErrorReceived,
                            new YeelightProtocolErrorEventArgs(exception, frame),
                            nameof(ProtocolErrorReceived));
                        continue;
                    }

                    DispatchMessage(message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A local disconnect owns cleanup and pending-request completion.
        }
        catch (IOException exception)
        {
            HandleConnectionLost(generation, exception);
        }
        catch (SocketException exception)
        {
            HandleConnectionLost(generation, exception);
        }
        catch (ObjectDisposedException exception)
        {
            HandleConnectionLost(generation, exception);
        }
        catch (YeelightProtocolException exception)
        {
            HandleConnectionLost(generation, exception);
        }
    }

    private void DispatchMessage(YeelightMessage message)
    {
        if (message is YeelightResponse response)
        {
            TaskCompletionSource<YeelightResponse>? completion = null;

            lock (_sync)
            {
                if (_pending.Remove(response.Id, out TaskCompletionSource<YeelightResponse>? found))
                {
                    completion = found;
                }
            }

            completion?.TrySetResult(response);

            if (completion is null)
            {
                InvokeSafely(
                    UnmatchedResponseReceived,
                    new YeelightUnmatchedResponseEventArgs(response),
                    nameof(UnmatchedResponseReceived));
            }

            return;
        }

        if (message is YeelightNotification notification)
        {
            InvokeSafely(
                NotificationReceived,
                new YeelightNotificationEventArgs(notification),
                nameof(NotificationReceived));
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Arbitrary consumer callbacks must not terminate the TCP receive loop.")]
    private void InvokeSafely<TEventArgs>(
        EventHandler<TEventArgs>? subscribers,
        TEventArgs eventArgs,
        string eventName)
        where TEventArgs : EventArgs
    {
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((EventHandler<TEventArgs>)subscriber)(this, eventArgs);
            }
            catch (Exception exception)
            {
                ReportSubscriberError(eventName, exception);
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This final observer boundary cannot recursively report its own callback failures.")]
    private void ReportSubscriberError(string eventName, Exception exception)
    {
        EventHandler<YeelightSubscriberErrorEventArgs>? subscribers =
            SubscriberErrorReceived;

        if (subscribers is null)
        {
            return;
        }

        var eventArgs = new YeelightSubscriberErrorEventArgs(
            eventName,
            exception);

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((EventHandler<YeelightSubscriberErrorEventArgs>)subscriber)(
                    this,
                    eventArgs);
            }
            catch (Exception)
            {
                // A terminal diagnostic callback cannot be allowed to break
                // transport processing or recursively report itself.
            }
        }
    }

    private void HandleConnectionLost(int generation, Exception reason)
    {
        TcpClient? tcpClient;
        CancellationTokenSource? connectionCancellation;
        List<TaskCompletionSource<YeelightResponse>> pending;

        lock (_sync)
        {
            if (_connectionGeneration != generation || _stream is null)
            {
                return;
            }

            tcpClient = _tcpClient;
            connectionCancellation = _connectionCancellation;
            _tcpClient = null;
            _stream = null;
            _connectionCancellation = null;
            _receiveLoop = null;
            _connectionGeneration++;
            pending = DrainPendingRequests();
        }

        connectionCancellation?.Cancel();
        tcpClient?.Dispose();
        connectionCancellation?.Dispose();

        var exception = reason as YeelightDisconnectedException
            ?? new YeelightDisconnectedException(
                "The Yeelight connection was lost.",
                reason);

        CompletePendingRequests(pending, exception);
    }

    private List<TaskCompletionSource<YeelightResponse>> DrainPendingRequests()
    {
        var pending = _pending.Values.ToList();
        _pending.Clear();
        return pending;
    }

    private static void CompletePendingRequests(
        IEnumerable<TaskCompletionSource<YeelightResponse>> requests,
        Exception exception)
    {
        foreach (TaskCompletionSource<YeelightResponse> request in requests)
        {
            request.TrySetException(exception);
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timeout,
                "The request timeout must be a finite positive duration.");
        }
    }

    private int GetNextRequestId()
    {
        int requestId = Interlocked.Increment(ref _nextRequestId);

        if (requestId > 0)
        {
            return requestId;
        }

        lock (_sync)
        {
            if (_nextRequestId <= 0)
            {
                _nextRequestId = 1;
            }

            return _nextRequestId;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record ConnectionTarget(string Host, int Port);
}
