using System.Diagnostics;

namespace LibraTray.Core.Networking;

public sealed record YeelightCommandTrace(
    long Sequence,
    DateTimeOffset StartedUtc,
    string Method,
    IReadOnlyList<object?> Parameters,
    IReadOnlyList<string>? Results,
    TimeSpan Elapsed,
    string? FailureType);

/// <summary>
/// Emits a best-effort trace after each actual transport attempt. The callback
/// must never be allowed to change device-command behavior.
/// </summary>
internal sealed class TracingYeelightCommandTransport(
    IYeelightCommandTransport inner,
    Action<YeelightCommandTrace> trace) : IYeelightCommandTransport
{
    private readonly IYeelightCommandTransport _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Action<YeelightCommandTrace> _trace =
        trace ?? throw new ArgumentNullException(nameof(trace));
    private long _sequence;

    public async Task<IReadOnlyList<string>> SendAsync(
        string method,
        IReadOnlyList<object?> parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        long startedTimestamp = Stopwatch.GetTimestamp();
        IReadOnlyList<string>? results = null;
        string? failureType = null;
        try
        {
            results = await _inner.SendAsync(
                    method,
                    parameters,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            return results;
        }
        catch (Exception exception)
        {
            failureType = exception.GetType().Name;
            throw;
        }
        finally
        {
            TryTrace(new YeelightCommandTrace(
                Interlocked.Increment(ref _sequence),
                startedUtc,
                method,
                parameters.ToArray(),
                results?.ToArray(),
                Stopwatch.GetElapsedTime(startedTimestamp),
                failureType));
        }
    }

    private void TryTrace(YeelightCommandTrace entry)
    {
        try
        {
            _trace(entry);
        }
        catch (Exception)
        {
            // Diagnostic callbacks are deliberately isolated from commands.
        }
    }
}
