using System.Diagnostics;

namespace LibraTray.Core.Networking;

/// <summary>
/// Serializes all traffic for one Yeelight connection, spaces short bursts,
/// and keeps a rolling-window ceiling below the published connection quota.
/// </summary>
internal sealed class RateLimitedYeelightCommandTransport :
    IYeelightCommandTransport,
    IDisposable
{
    private readonly IYeelightCommandTransport _inner;
    private readonly TimeSpan _minimumInterval;
    private readonly int _maximumCommandsPerWindow;
    private readonly TimeSpan _quotaWindow;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Queue<long> _sendTimestamps = [];
    private long _lastSendTimestamp;
    private bool _disposed;

    public RateLimitedYeelightCommandTransport(
        IYeelightCommandTransport inner,
        TimeSpan minimumInterval,
        int maximumCommandsPerWindow,
        TimeSpan quotaWindow)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (minimumInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumInterval),
                "The minimum command interval cannot be negative.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumCommandsPerWindow,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            quotaWindow,
            TimeSpan.Zero);
        _inner = inner;
        _minimumInterval = minimumInterval;
        _maximumCommandsPerWindow = maximumCommandsPerWindow;
        _quotaWindow = quotaWindow;
    }

    public async Task<IReadOnlyList<string>> SendAsync(
        string method,
        IReadOnlyList<object?> parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WaitForQuotaAsync(cancellationToken).ConfigureAwait(false);
            return await _inner
                .SendAsync(
                    method,
                    parameters,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task WaitForQuotaAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            long now = Stopwatch.GetTimestamp();
            RemoveExpiredTimestamps(now);

            TimeSpan intervalDelay = _lastSendTimestamp == 0
                ? TimeSpan.Zero
                : _minimumInterval
                    - Stopwatch.GetElapsedTime(_lastSendTimestamp, now);
            TimeSpan quotaDelay = _sendTimestamps.Count
                    < _maximumCommandsPerWindow
                ? TimeSpan.Zero
                : _quotaWindow
                    - Stopwatch.GetElapsedTime(_sendTimestamps.Peek(), now);
            TimeSpan delay = intervalDelay > quotaDelay
                ? intervalDelay
                : quotaDelay;
            if (delay <= TimeSpan.Zero)
            {
                long sentAt = Stopwatch.GetTimestamp();
                _lastSendTimestamp = sentAt;
                _sendTimestamps.Enqueue(sentAt);
                return;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private void RemoveExpiredTimestamps(long now)
    {
        while (_sendTimestamps.TryPeek(out long timestamp)
            && Stopwatch.GetElapsedTime(timestamp, now) >= _quotaWindow)
        {
            _ = _sendTimestamps.Dequeue();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
