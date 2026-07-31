using System.Globalization;
using System.Text.Json;
using LibraTray.Core.Protocol;

namespace LibraTray.Core.Networking;

/// <summary>
/// Provides a UI-independent command boundary for device adapters.
/// </summary>
public interface IYeelightCommandTransport
{
    /// <summary>
    /// Sends one Yeelight command and returns its result values as invariant
    /// strings. Empty and null result values are represented as empty strings.
    /// </summary>
    Task<IReadOnlyList<string>> SendAsync(
        string method,
        IReadOnlyList<object?> parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapts <see cref="YeelightClient"/> to the command boundary consumed by
/// product-specific adapters.
/// </summary>
public sealed class YeelightCommandTransport : IYeelightCommandTransport
{
    private readonly YeelightClient _client;

    public YeelightCommandTransport(YeelightClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<IReadOnlyList<string>> SendAsync(
        string method,
        IReadOnlyList<object?> parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(parameters);

        YeelightSuccessResponse response = await _client
            .SendCommandAsync(method, parameters, timeout, cancellationToken)
            .ConfigureAwait(false);

        return response.Results
            .Select(ConvertResult)
            .ToArray();
    }

    private static string ConvertResult(JsonElement result) =>
        result.ValueKind switch
        {
            JsonValueKind.String => result.GetString() ?? string.Empty,
            JsonValueKind.Number => result.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            JsonValueKind.Null => string.Empty,
            _ => throw new YeelightProtocolException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unsupported command result kind: {result.ValueKind}.")),
        };
}
