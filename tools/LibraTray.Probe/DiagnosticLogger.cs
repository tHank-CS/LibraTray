using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LibraTray.Probe;

internal sealed class DiagnosticLogger : IAsyncDisposable
{
    public const long MaximumLogBytes = 10L * 1024 * 1024;

    private static readonly UTF8Encoding Utf8WithoutBom =
        new(encoderShouldEmitUTF8Identifier: false);

    private readonly FileStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _sensitiveValuesLock = new();
    private readonly Dictionary<string, string> _sensitiveValues =
        new(StringComparer.Ordinal);
    private readonly bool _redact;
    private long _bytesWritten;
    private bool _limitWarningShown;

    private DiagnosticLogger(string path, FileStream stream, bool redact)
    {
        Path = path;
        _stream = stream;
        _redact = redact;
        _bytesWritten = stream.Length;
    }

    public string Path { get; }

    public bool RedactionEnabled => _redact;

    internal string SanitizeRawForConsole(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string redacted = _redact
            ? DiagnosticRedactor.Redact(value)
            : DiagnosticRedactor.RedactCredentials(value);
        return RedactKnownValues(redacted);
    }

    public void RegisterSensitiveValue(
        string? value,
        string replacement = "[REDACTED-SENSITIVE]",
        bool alwaysRedact = false)
    {
        if ((!_redact && !alwaysRedact) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        lock (_sensitiveValuesLock)
        {
            _sensitiveValues[value] = replacement;
            _sensitiveValues[JsonEncodedText.Encode(value).ToString()] = replacement;
            _sensitiveValues[EscapeJsonStringMinimally(value)] = replacement;
        }
    }

    public static DiagnosticLogger Create(string? requestedPath, bool redact)
    {
        string path = requestedPath ?? CreateDefaultPath();
        string fullPath = System.IO.Path.GetFullPath(path);
        string? directory = System.IO.Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("无法确定诊断日志目录。");
        }

        Directory.CreateDirectory(directory);

        var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return new DiagnosticLogger(fullPath, stream, redact);
    }

    public async ValueTask WriteAsync(
        string eventName,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var safeData = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (data is not null)
        {
            foreach ((string key, object? value) in data)
            {
                safeData[key] = Sanitize(key, value);
            }
        }

        var record = new LogRecord(
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            eventName,
            safeData);
        string json = JsonSerializer.Serialize(record, ProbeJsonContext.Default.LogRecord);
        byte[] line = Utf8WithoutBom.GetBytes(json + "\n");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_bytesWritten + line.Length > MaximumLogBytes)
            {
                WarnLogLimitOnce();
                return;
            }

            await _stream
                .WriteAsync(line, cancellationToken)
                .ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            _bytesWritten += line.Length;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }
    }

    private static string CreateDefaultPath()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new IOException(
                "无法解析 %LOCALAPPDATA%，请使用 --log-path 显式指定日志文件。");
        }

        string fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"protocol-probe-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.jsonl");
        return System.IO.Path.Combine(localAppData, "LibraTray", "logs", fileName);
    }

    private object? Sanitize(string key, object? value)
    {
        if (value is null)
        {
            return value;
        }

        if (IsSensitiveCredentialKey(key))
        {
            return "[REDACTED-SECRET]";
        }

        if (!_redact)
        {
            return value switch
            {
                string text => RedactKnownValues(
                    DiagnosticRedactor.RedactCredentials(text)),
                JsonElement element => RedactKnownValues(
                    DiagnosticRedactor.RedactCredentials(element.GetRawText())),
                _ => value,
            };
        }

        if (IsSensitiveIdentifierKey(key))
        {
            return "[REDACTED-DEVICE-ID]";
        }

        if (IsSensitiveNetworkKey(key))
        {
            return NormalizeKey(key) == "bssid"
                ? "[REDACTED-MAC]"
                : "[REDACTED-NETWORK]";
        }

        if (IsSensitiveHostKey(key))
        {
            return "[REDACTED-HOSTNAME]";
        }

        if (IsSensitiveNameKey(key))
        {
            return "[REDACTED-DEVICE-NAME]";
        }

        return value switch
        {
            string text => RedactKnownValues(DiagnosticRedactor.Redact(text)),
            JsonElement element => RedactKnownValues(
                DiagnosticRedactor.Redact(element.GetRawText())),
            IFormattable formattable => formattable.ToString(
                format: null,
                CultureInfo.InvariantCulture),
            _ => RedactKnownValues(
                DiagnosticRedactor.Redact(value.ToString() ?? string.Empty)),
        };
    }

    private string RedactKnownValues(string value)
    {
        lock (_sensitiveValuesLock)
        {
            foreach ((string sensitiveValue, string replacement) in _sensitiveValues
                .OrderByDescending(pair => pair.Key.Length))
            {
                value = value.Replace(
                    sensitiveValue,
                    replacement,
                    StringComparison.Ordinal);
            }
        }

        return value;
    }

    private static bool IsSensitiveIdentifierKey(string key) =>
        NormalizeKey(key) is "id" or "deviceid";

    private static bool IsSensitiveCredentialKey(string key) =>
        !string.IsNullOrWhiteSpace(key)
        && DiagnosticRedactor.IsCredentialPropertyName(key);

    private static bool IsSensitiveNetworkKey(string key) =>
        NormalizeKey(key) is "ssid" or "bssid";

    private static bool IsSensitiveHostKey(string key) =>
        NormalizeKey(key) is "hostname";

    private static bool IsSensitiveNameKey(string key) =>
        NormalizeKey(key) is
            "name"
            or "alias"
            or "reportedname"
            or "displayname"
            or "useralias"
            or "devicename";

    private static string NormalizeKey(string key) =>
        key
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string EscapeJsonStringMinimally(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(
                character switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => character.ToString(),
                });
        }

        return builder.ToString();
    }

    private void WarnLogLimitOnce()
    {
        if (_limitWarningShown)
        {
            return;
        }

        _limitWarningShown = true;
        Console.Error.WriteLine(
            "警告：诊断日志已达到 10 MiB 单文件硬上限；本次运行将停止记录，"
            + "网络监听仍可继续，避免磁盘无限增长。");
    }

    internal sealed record LogRecord(
        string Timestamp,
        string Event,
        IReadOnlyDictionary<string, object?> Data);
}

[JsonSerializable(typeof(DiagnosticLogger.LogRecord))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;

internal static partial class DiagnosticRedactor
{
    private const int MaximumNestedRedactionDepth = 8;

    public static string RedactCredentials(string input) =>
        RedactCredentials(input, depth: 0);

    private static string RedactCredentials(string input, int depth)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (depth >= MaximumNestedRedactionDepth)
        {
            return "[REDACTED-NESTED-DATA]";
        }

        if (TryRedactCredentialJson(
                input,
                value => RedactCredentials(value, depth + 1),
                out string? json))
        {
            return json;
        }

        if (ContainsJsonContainerForSensitiveKey(
                input,
                IsCredentialPropertyName))
        {
            return "[REDACTED-SECRET-CONTAINER]";
        }

        string result = JsonCredentialValueRegex().Replace(
            input,
            RedactJsonCredentialMatch);
        result = HeaderCredentialValueRegex().Replace(
            result,
            RedactHeaderCredentialMatch);
        return AssignmentValueRegex().Replace(
            result,
            RedactAssignmentCredentialMatch);
    }

    private static bool TryRedactCredentialJson(
        string input,
        Func<string, string> sanitizeString,
        out string redacted) =>
        TryRedactJson(
            input,
            GetCredentialReplacement,
            sanitizeString,
            out redacted);

    private static bool TryRedactSensitiveJson(
        string input,
        Func<string, string> sanitizeString,
        out string redacted) =>
        TryRedactJson(
            input,
            GetSensitiveReplacement,
            sanitizeString,
            out redacted);

    private static bool TryRedactJson(
        string input,
        Func<string, JsonElement, string?> getReplacement,
        Func<string, string> sanitizeString,
        out string redacted)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            bool changed = WriteRedactedJson(
                document.RootElement,
                writer,
                getReplacement,
                sanitizeString);
            writer.Flush();
            redacted = changed
                ? Encoding.UTF8.GetString(buffer.WrittenSpan)
                : input;
            return true;
        }
        catch (JsonException)
        {
            redacted = input;
            return false;
        }
        catch (InvalidOperationException)
        {
            redacted = input;
            return false;
        }
    }

    private static bool WriteRedactedJson(
        JsonElement element,
        Utf8JsonWriter writer,
        Func<string, JsonElement, string?> getReplacement,
        Func<string, string> sanitizeString)
    {
        bool changed = false;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    string? replacement = getReplacement(
                        property.Name,
                        property.Value);
                    if (replacement is not null)
                    {
                        writer.WriteStringValue(replacement);
                        changed = true;
                    }
                    else
                    {
                        changed |= WriteRedactedJson(
                            property.Value,
                            writer,
                            getReplacement,
                            sanitizeString);
                    }
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    changed |= WriteRedactedJson(
                        item,
                        writer,
                        getReplacement,
                        sanitizeString);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                string value = element.GetString() ?? string.Empty;
                string sanitized = sanitizeString(value);
                writer.WriteStringValue(sanitized);
                changed = !string.Equals(
                    value,
                    sanitized,
                    StringComparison.Ordinal);
                break;

            default:
                element.WriteTo(writer);
                break;
        }

        return changed;
    }

    private static string? GetCredentialReplacement(
        string propertyName,
        JsonElement _) =>
        !string.IsNullOrWhiteSpace(propertyName)
        && IsCredentialPropertyName(propertyName)
            ? "[REDACTED-SECRET]"
            : null;

    private static string? GetSensitiveReplacement(
        string propertyName,
        JsonElement value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        if (IsCredentialPropertyName(propertyName))
        {
            return "[REDACTED-SECRET]";
        }

        string normalized = NormalizePropertyName(propertyName);
        if (normalized == "id" && value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return GetSensitiveTextReplacement(propertyName);
    }

    private static string? GetSensitiveTextReplacement(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        if (IsCredentialPropertyName(propertyName))
        {
            return "[REDACTED-SECRET]";
        }

        string normalized = NormalizePropertyName(propertyName);
        return normalized is
            "id"
            or "deviceid"
                ? "[REDACTED-DEVICE-ID]"
                : normalized is
            "name"
            or "alias"
            or "reportedname"
            or "displayname"
            or "useralias"
            or "devicename"
            or "ssid"
            or "bssid"
            or "hostname"
                ? "[REDACTED-SENSITIVE]"
                : null;
    }

    private static string RedactJsonCredentialMatch(Match match)
    {
        string? propertyName;
        try
        {
            propertyName = JsonSerializer.Deserialize<string>(
                match.Groups["key"].Value);
        }
        catch (JsonException)
        {
            return match.Value;
        }

        return !string.IsNullOrWhiteSpace(propertyName)
            && IsCredentialPropertyName(propertyName)
                ? string.Concat(
                    match.Groups["prefix"].Value,
                    "\"[REDACTED-SECRET]\"")
                : match.Value;
    }

    private static string RedactHeaderCredentialMatch(Match match) =>
        IsCredentialTextKey(match.Groups["key"].Value)
            ? string.Concat(
                match.Groups["prefix"].Value,
                "[REDACTED-SECRET]")
            : match.Value;

    private static string RedactHeaderSensitiveMatch(Match match)
    {
        string? replacement = GetSensitiveTextReplacement(
            DecodeTextKey(match.Groups["key"].Value));
        return replacement is null
            ? match.Value
            : string.Concat(match.Groups["prefix"].Value, replacement);
    }

    private static string RedactAssignmentCredentialMatch(Match match) =>
        IsCredentialTextKey(match.Groups["key"].Value)
            ? string.Concat(
                match.Groups["prefix"].Value,
                "[REDACTED-SECRET]")
            : match.Value;

    private static string RedactAssignmentSensitiveMatch(Match match)
    {
        string? replacement = GetSensitiveTextReplacement(
            DecodeTextKey(match.Groups["key"].Value));
        return replacement is null
            ? match.Value
            : string.Concat(match.Groups["prefix"].Value, replacement);
    }

    private static bool ContainsJsonContainerForSensitiveKey(
        string input,
        Func<string, bool> isSensitive)
    {
        foreach (Match match in JsonContainerValueRegex().Matches(input))
        {
            try
            {
                string? propertyName = JsonSerializer.Deserialize<string>(
                    match.Groups["key"].Value);
                if (!string.IsNullOrWhiteSpace(propertyName)
                    && isSensitive(propertyName))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // Continue scanning other well-formed key tokens.
            }
        }

        return false;
    }

    private static string DecodeTextKey(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static bool IsCredentialTextKey(string value)
    {
        string decoded = DecodeTextKey(value);
        return !string.IsNullOrWhiteSpace(decoded)
            && IsCredentialPropertyName(decoded);
    }

    public static bool IsSensitivePropertyName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = NormalizePropertyName(name);
        return normalized is
            "name"
            or "alias"
            or "reportedname"
            or "displayname"
            or "useralias"
            or "devicename"
            or "id"
            or "deviceid"
            or "ssid"
            or "bssid"
            or "hostname"
            || IsCredentialPropertyName(name);
    }

    public static bool IsCredentialPropertyName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = NormalizePropertyName(name);
        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("passwd", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("cookie", StringComparison.Ordinal);
    }

    public static string RedactSensitiveResultValues(
        string rawJson,
        IReadOnlyList<int> sensitiveIndexes)
    {
        ArgumentNullException.ThrowIfNull(rawJson);
        ArgumentNullException.ThrowIfNull(sensitiveIndexes);

        HashSet<int> indexes = sensitiveIndexes
            .Where(static index => index >= 0)
            .ToHashSet();
        if (indexes.Count == 0)
        {
            return rawJson;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return rawJson;
            }

            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            bool changed = WriteRedactedResultObject(
                document.RootElement,
                writer,
                indexes);
            writer.Flush();
            return changed
                ? Encoding.UTF8.GetString(buffer.WrittenSpan)
                : rawJson;
        }
        catch (JsonException)
        {
            return rawJson;
        }
        catch (InvalidOperationException)
        {
            return rawJson;
        }
    }

    private static bool WriteRedactedResultObject(
        JsonElement response,
        Utf8JsonWriter writer,
        HashSet<int> sensitiveIndexes)
    {
        bool changed = false;
        writer.WriteStartObject();
        foreach (JsonProperty property in response.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);
            if (!string.Equals(
                    property.Name,
                    "result",
                    StringComparison.Ordinal)
                || property.Value.ValueKind != JsonValueKind.Array)
            {
                property.Value.WriteTo(writer);
                continue;
            }

            writer.WriteStartArray();
            int index = 0;
            foreach (JsonElement item in property.Value.EnumerateArray())
            {
                if (sensitiveIndexes.Contains(index))
                {
                    writer.WriteStringValue("[REDACTED-SENSITIVE]");
                    changed = true;
                }
                else
                {
                    item.WriteTo(writer);
                }

                index++;
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        return changed;
    }

    public static string Redact(string input) =>
        Redact(input, depth: 0);

    private static string Redact(string input, int depth)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (depth >= MaximumNestedRedactionDepth)
        {
            return "[REDACTED-NESTED-DATA]";
        }

        string result;
        if (TryRedactSensitiveJson(
                input,
                value => Redact(value, depth + 1),
                out string json))
        {
            result = json;
        }
        else if (ContainsJsonContainerForSensitiveKey(
                     input,
                     static key => GetSensitiveTextReplacement(key) is not null))
        {
            return "[REDACTED-SENSITIVE-CONTAINER]";
        }
        else
        {
            result = RedactCredentials(input, depth);
        }

        result = HeaderCredentialValueRegex().Replace(
            result,
            RedactHeaderSensitiveMatch);
        result = AssignmentValueRegex().Replace(
            result,
            RedactAssignmentSensitiveMatch);
        result = WindowsAbsolutePathRegex().Replace(result, "[REDACTED-PATH]");
        result = MacAddressRegex().Replace(result, "[REDACTED-MAC]");
        result = JsonSensitiveValueRegex().Replace(
            result,
            static match => string.Concat(
                match.Groups[1].Value,
                "[REDACTED-SENSITIVE]",
                match.Groups[2].Value));
        result = HeaderSensitiveValueRegex().Replace(
            result,
            static match => string.Concat(
                match.Groups[1].Value,
                "[REDACTED-SENSITIVE]"));
        result = JsonDeviceIdRegex().Replace(
            result,
            static match => string.Concat(
                match.Groups[1].Value,
                "[REDACTED-DEVICE-ID]",
                match.Groups[2].Value));
        result = DiscoveryDeviceIdRegex().Replace(
            result,
            static match => string.Concat(match.Groups[1].Value, "[REDACTED-DEVICE-ID]"));
        result = Ipv6CandidateRegex().Replace(
            result,
            static match =>
            {
                string address = match.Groups["address"].Value;
                int zoneIndex = address.IndexOf('%', StringComparison.Ordinal);
                string parseableAddress = zoneIndex >= 0
                    ? address[..zoneIndex]
                    : address;
                return IPAddress.TryParse(parseableAddress, out IPAddress? parsed)
                    && parsed.AddressFamily
                        == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? "[REDACTED-IP]"
                    : match.Value;
            });
        result = Ipv4AddressRegex().Replace(result, "[REDACTED-IP]");
        result = result.Replace(
            Environment.UserName,
            "[REDACTED-USERNAME]",
            StringComparison.OrdinalIgnoreCase);

        string hostName = Dns.GetHostName();
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            result = result.Replace(
                hostName,
                "[REDACTED-HOSTNAME]",
                StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string NormalizePropertyName(string name) =>
        name
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    [GeneratedRegex(
        @"(?i)(?:[A-Z]:[\\/]|\\\\)[^""'\r\n,\]}]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAbsolutePathRegex();

    [GeneratedRegex(
        @"(?<prefix>(?<key>""(?:\\.|[^""\\])*"")[ \t\r\n]*:[ \t\r\n]*)"
        + @"(?:""(?:\\.|[^""\\])*""|[^,}\r\n]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex JsonCredentialValueRegex();

    [GeneratedRegex(
        @"(?<key>""(?:\\.|[^""\\])*"")[ \t\r\n]*:[ \t\r\n]*[\[{]",
        RegexOptions.CultureInvariant)]
    private static partial Regex JsonContainerValueRegex();

    [GeneratedRegex(
        @"^(?<prefix>[ \t]*(?<key>[!#$%&'*+\-.^_`|~A-Z0-9]+)"
        + @"[ \t]*:[ \t]*)[^\r\n]*(?=\r?$)",
        RegexOptions.CultureInvariant
            | RegexOptions.IgnoreCase
            | RegexOptions.Multiline)]
    private static partial Regex HeaderCredentialValueRegex();

    [GeneratedRegex(
        @"(?<prefix>(?<key>[A-Z0-9_.%-]+)[ \t]*=[ \t]*)"
        + @"(?:""(?:\\.|[^""\\])*""|'[^'\r\n]*'"
        + @"|[^&;,\r\n]*?(?=[&;,]|\r?$|[ \t]+[A-Z0-9_.%-]+[ \t]*=))",
        RegexOptions.CultureInvariant
            | RegexOptions.IgnoreCase
            | RegexOptions.Multiline)]
    private static partial Regex AssignmentValueRegex();

    [GeneratedRegex(
        @"\b(?:(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}"
        + @"|(?:[0-9A-Fa-f]{4}\.){2}[0-9A-Fa-f]{4})\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex(
        @"(?i)(""(?:name|alias|reportedName|displayName|userAlias|user_alias|"
        + @"deviceName|device_name|ssid|bssid|hostname|host_name|password|passwd|"
        + @"token|access_token|refresh_token)""\s*:\s*"")"
        + @"(?:\\.|[^""\\])*("")",
        RegexOptions.CultureInvariant)]
    private static partial Regex JsonSensitiveValueRegex();

    [GeneratedRegex(
        @"(?im)^(\s*(?:name|alias|reportedName|displayName|userAlias|user_alias|"
        + @"deviceName|device_name|device-name|ssid|bssid|hostname|host_name|"
        + @"password|passwd|token|access_token|refresh_token)\s*:\s*).*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HeaderSensitiveValueRegex();

    [GeneratedRegex(
        @"(?i)(""(?:device[_-]?id|id)""\s*:\s*"")"
        + @"(?:\\.|[^""\\])*("")",
        RegexOptions.CultureInvariant)]
    private static partial Regex JsonDeviceIdRegex();

    [GeneratedRegex(
        @"(?im)(\b(?:device[_-]?id|id)\s*:\s*)(?:0x[0-9a-f]+|[a-z0-9][a-z0-9._:-]{7,})",
        RegexOptions.CultureInvariant)]
    private static partial Regex DiscoveryDeviceIdRegex();

    [GeneratedRegex(
        @"(?<!\d)(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4AddressRegex();

    [GeneratedRegex(
        @"(?<![0-9A-Za-z])(?:\[(?<address>[0-9A-Fa-f:.]+"
        + @"(?:%[0-9A-Za-z_.-]+)?)\]|(?<address>[0-9A-Fa-f:.]*:"
        + @"[0-9A-Fa-f:.]*(?:%[0-9A-Za-z_.-]+)?))(?![0-9A-Za-z])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6CandidateRegex();
}
