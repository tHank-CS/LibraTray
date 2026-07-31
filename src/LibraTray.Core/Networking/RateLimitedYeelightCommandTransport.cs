using System.Diagnostics;

namespace LibraTray.Core.Networking;

/// <summary>
/// Serializes all traffic for one Yeelight connection and enforces a minimum
/// start-to-start interval below the device's published per-connection quota.
/// </summary>
internal sealed class RateLimitedYeelightCommandTransport :
    IYeelightCommandTransport,
    IDisposable
{
    private readonly IYeelightCommandTransport _inner;
    private readonly TimeSpan _minimumInterval;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _lastSendTimestamp;
    private bool _disposed;

    public RateLimitedYeelightCommandTransport(
        IYeelightCommandTransport inner,
        TimeSpan minimumInterval)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (minimumInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumInterval),
                "The minimum command interval cannot be negative.");
        }

        _inner = inner;
        _minimumInterval = minimumInterval;
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
            long now = Stopwatch.GetTimestamp();
            if (_lastSendTimestamp != 0)
            {
                TimeSpan elapsed = Stopwatch.GetElapsedTime(
                    _lastSendTimestamp,
                    now);
                TimeSpan remaining = _minimumInterval - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            _lastSendTimestamp = Stopwatch.GetTimestamp();
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
