using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using LibraTray.Core.Identity;
using LibraTray.Core.Protocol;

namespace LibraTray.Probe;

internal sealed class ProbeOutput
{
    private readonly DiagnosticLogger _logger;

    public ProbeOutput(DiagnosticLogger logger)
    {
        _logger = logger;
    }

    public bool RedactionEnabled => _logger.RedactionEnabled;

    public void RegisterSensitiveValue(
        string? value,
        string replacement = "[REDACTED-SENSITIVE]",
        bool alwaysRedact = false) =>
        _logger.RegisterSensitiveValue(value, replacement, alwaysRedact);

    public void RegisterConnectionTarget(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)
            || IPAddress.TryParse(host.Trim('[', ']'), out _))
        {
            return;
        }

        RegisterSensitiveValue(host, "[REDACTED-HOSTNAME]");
    }

    public async ValueTask InfoAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        WriteConsole("INFO", message);
        await _logger.WriteAsync(
            "info",
            new Dictionary<string, object?> { ["message"] = message },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WarningAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        WriteConsole("WARN", message, ConsoleColor.Yellow);
        await _logger.WriteAsync(
            "warning",
            new Dictionary<string, object?> { ["message"] = message },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ErrorAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        WriteConsole("ERROR", message, ConsoleColor.Red);
        await _logger.WriteAsync(
            "error",
            new Dictionary<string, object?> { ["message"] = message },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SentJsonAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        WriteConsole("TX", $"JSON: {json}", ConsoleColor.Cyan);
        await _logger.WriteAsync(
            "sent-json",
            new Dictionary<string, object?> { ["json"] = json },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SentPayloadAsync(
        string transport,
        string payload,
        CancellationToken cancellationToken = default)
    {
        WriteConsole("TX", $"{transport} RAW:\n{payload}", ConsoleColor.Cyan);
        await _logger.WriteAsync(
            "sent-payload",
            new Dictionary<string, object?>
            {
                ["transport"] = transport,
                ["raw"] = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RawResponseAsync(
        string transport,
        string raw,
        CancellationToken cancellationToken = default)
    {
        await RawResponseAsync(
            transport,
            raw,
            raw,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RawResponseAsync(
        string transport,
        string consoleRaw,
        string logRaw,
        CancellationToken cancellationToken = default)
    {
        string safeConsoleRaw = _logger.SanitizeRawForConsole(consoleRaw);
        WriteConsole(
            "RX",
            $"{transport} RAW:\n{safeConsoleRaw}",
            ConsoleColor.Green);
        await _logger.WriteAsync(
            "raw-response",
            new Dictionary<string, object?>
            {
                ["transport"] = transport,
                ["raw"] = logRaw,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PropertiesAsync(
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        string formatted = string.Join(
            ", ",
            properties.Select(pair =>
                DiagnosticRedactor.IsCredentialPropertyName(pair.Key)
                    ? $"{pair.Key}=[REDACTED-SECRET]"
                    : $"{pair.Key}={DiagnosticRedactor.RedactCredentials(pair.Value)}"));
        WriteConsole("PROPS", formatted, ConsoleColor.Magenta);

        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, string value) in properties)
        {
            data[key] = value;
        }

        await _logger.WriteAsync("props", data, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask IdentityAsync(
        DeviceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        string message =
            $"显示名：{identity.DisplayName} | 友好产品名：{identity.FriendlyProductName ?? "未知"}"
            + $" | 硬件型号：{identity.HardwareModel ?? "未知"}"
            + $" | 内部型号：{identity.InternalModel ?? "未知"}"
            + $" | 设备上报名：{identity.ReportedName ?? "未提供"}";
        WriteConsole("IDENTITY", message, ConsoleColor.Blue);

        await _logger.WriteAsync(
            "identity",
            new Dictionary<string, object?>
            {
                ["displayName"] = identity.DisplayName,
                ["friendlyProductName"] = identity.FriendlyProductName,
                ["hardwareModel"] = identity.HardwareModel,
                ["internalModel"] = identity.InternalModel,
                ["reportedName"] = identity.ReportedName,
                ["knownProduct"] = identity.IsKnownProduct,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DiscoveryAsync(
        IPEndPoint sender,
        YeelightDiscoveryResponse response,
        CancellationToken cancellationToken = default)
    {
        WriteConsole(
            "DISCOVERY",
            $"发送端 {sender} | 控制端点 {response.ControlEndPoint}",
            ConsoleColor.DarkCyan);

        foreach ((string name, string value) in response.Headers)
        {
            string safeValue = DiagnosticRedactor.IsCredentialPropertyName(name)
                ? "[REDACTED-SECRET]"
                : DiagnosticRedactor.RedactCredentials(value);
            Console.WriteLine(EscapeForConsole($"  {name}: {safeValue}"));
        }

        await _logger.WriteAsync(
            "discovery",
            new Dictionary<string, object?>
            {
                ["sender"] = sender.ToString(),
                ["controlEndpoint"] = response.ControlEndPoint.ToString(),
                ["deviceId"] = response.Id,
                ["model"] = response.Model,
                ["reportedName"] = response.ReportedName,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ProtocolMessageAsync(
        YeelightMessage message,
        IReadOnlyList<int>? credentialResultIndexes = null,
        CancellationToken cancellationToken = default)
    {
        switch (message)
        {
            case YeelightSuccessResponse success:
                string results = FormatSuccessResults(
                    success,
                    credentialResultIndexes);
                await InfoAsync(
                    $"响应 id={success.Id.ToString(CultureInfo.InvariantCulture)} result=[{results}]",
                    cancellationToken).ConfigureAwait(false);
                break;

            case YeelightErrorResponse error:
                await ErrorAsync(
                    $"响应 id={error.Id.ToString(CultureInfo.InvariantCulture)}"
                    + $" error={error.Code.ToString(CultureInfo.InvariantCulture)}"
                    + $" message={error.Message}",
                    cancellationToken).ConfigureAwait(false);
                break;

            case YeelightPropsNotification props:
                var values = props.Properties.ToDictionary(
                    pair => pair.Key,
                    pair => FormatJsonElement(pair.Value),
                    StringComparer.Ordinal);
                await PropertiesAsync(values, cancellationToken).ConfigureAwait(false);
                break;

            case YeelightUnknownNotification notification:
                await WarningAsync(
                    $"未知通知 method={notification.Method}",
                    cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public static string FormatJsonElement(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText();

    internal static string FormatSuccessResults(
        YeelightSuccessResponse success,
        IReadOnlyList<int>? credentialResultIndexes) =>
        string.Join(
            ", ",
            success.Results.Select((result, index) =>
                credentialResultIndexes?.Contains(index) == true
                    ? "[REDACTED-SECRET]"
                    : DiagnosticRedactor.RedactCredentials(
                        FormatJsonElement(result))));

    internal static string EscapeForConsole(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder? builder = null;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (!char.IsControl(character)
                || character is '\r' or '\n' or '\t')
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(value.Length + 8)
                .Append(value, 0, index);
            builder.Append(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"\\u{(int)character:X4}"));
        }

        return builder?.ToString() ?? value;
    }

    private static void WriteConsole(
        string category,
        string message,
        ConsoleColor? color = null)
    {
        string prefix = string.Create(
            CultureInfo.InvariantCulture,
            $"[{DateTimeOffset.Now:O}] [{category}] ");

        ConsoleColor originalColor = Console.ForegroundColor;
        try
        {
            if (color is not null && !Console.IsOutputRedirected)
            {
                Console.ForegroundColor = color.Value;
            }

            string safeMessage = DiagnosticRedactor.RedactCredentials(message);
            Console.WriteLine(prefix + EscapeForConsole(safeMessage));
        }
        finally
        {
            if (!Console.IsOutputRedirected)
            {
                Console.ForegroundColor = originalColor;
            }
        }
    }
}
