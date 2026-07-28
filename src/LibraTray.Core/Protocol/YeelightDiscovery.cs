using System.Collections.ObjectModel;
using System.Net;
using System.Text;

namespace LibraTray.Core.Protocol;

public sealed class YeelightDiscoveryResponse
{
    internal YeelightDiscoveryResponse(
        IPEndPoint controlEndPoint,
        IReadOnlyDictionary<string, string> headers)
    {
        ControlEndPoint = controlEndPoint;
        Headers = headers;
    }

    /// <summary>
    /// Gets the control endpoint exactly as advertised in the Location header.
    /// </summary>
    public IPEndPoint ControlEndPoint { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public string? Id => GetHeader("id");

    public string? Model => GetHeader("model");

    public string? ReportedName => GetHeader("name");

    private string? GetHeader(string name) =>
        Headers.TryGetValue(name, out string? value) ? value : null;
}

public static class YeelightDiscovery
{
    public const string MulticastAddress = "239.255.255.250";
    public const int MulticastPort = 1982;
    public const int MaximumDatagramBytes = 65_507;

    private const string RequestText =
        "M-SEARCH * HTTP/1.1\r\n"
        + "HOST: 239.255.255.250:1982\r\n"
        + "MAN: \"ssdp:discover\"\r\n"
        + "ST: wifi_bulb\r\n";
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] CreateRequest() => Encoding.ASCII.GetBytes(RequestText);

    public static YeelightDiscoveryResponse ParseResponse(ReadOnlySpan<byte> datagram)
    {
        if (datagram.IsEmpty)
        {
            throw new YeelightProtocolException("The discovery response is empty.");
        }

        if (datagram.Length > MaximumDatagramBytes)
        {
            throw new YeelightFrameTooLargeException(MaximumDatagramBytes);
        }

        string responseText;
        try
        {
            responseText = StrictUtf8.GetString(datagram);
        }
        catch (DecoderFallbackException exception)
        {
            throw new YeelightProtocolException(
                "The discovery response is not valid UTF-8.",
                exception);
        }
        string[] lines = responseText.Split(
            "\r\n",
            StringSplitOptions.None);

        if (lines.Length == 0 || !IsSuccessfulStatusLine(lines[0]))
        {
            throw new YeelightProtocolException(
                "The discovery response does not contain a successful HTTP-like status line.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Length == 0)
            {
                break;
            }

            int separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                throw new YeelightProtocolException(
                    $"The discovery response contains an invalid header at line {index + 1}.");
            }

            string name = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            if (name.Length == 0)
            {
                throw new YeelightProtocolException(
                    $"The discovery response contains an empty header name at line {index + 1}.");
            }

            if (!IsValidHeaderName(name) || !IsValidHeaderValue(value))
            {
                throw new YeelightProtocolException(
                    $"The discovery response contains an invalid header at line {index + 1}.");
            }

            if (headers.TryGetValue(name, out string? existingValue))
            {
                if (string.Equals(name, "Location", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        existingValue,
                        value,
                        StringComparison.Ordinal))
                {
                    throw new YeelightProtocolException(
                        $"The discovery response contains conflicting duplicate header '{name}'.");
                }

                continue;
            }

            headers.Add(name, value);
        }

        if (!headers.TryGetValue("Location", out string? locationValue)
            || !Uri.TryCreate(locationValue, UriKind.Absolute, out Uri? location)
            || !string.Equals(location.Scheme, "yeelight", StringComparison.OrdinalIgnoreCase)
            || location.UserInfo.Length > 0
            || !string.Equals(location.AbsolutePath, "/", StringComparison.Ordinal)
            || location.Query.Length > 0
            || location.Fragment.Length > 0
            || location.Port is <= 0 or > IPEndPoint.MaxPort
            || !IPAddress.TryParse(location.Host, out IPAddress? address))
        {
            throw new YeelightProtocolException(
                "The discovery response contains an invalid or missing Location header.");
        }

        var readOnlyHeaders = new ReadOnlyDictionary<string, string>(headers);
        return new YeelightDiscoveryResponse(
            new IPEndPoint(address, location.Port),
            readOnlyHeaders);
    }

    public static bool TryParseResponse(
        ReadOnlySpan<byte> datagram,
        out YeelightDiscoveryResponse? response)
    {
        try
        {
            response = ParseResponse(datagram);
            return true;
        }
        catch (YeelightProtocolException)
        {
            response = null;
            return false;
        }
    }

    private static bool IsSuccessfulStatusLine(string statusLine) =>
        string.Equals(statusLine, "HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            statusLine,
            "HTTP/1.1 200 OK",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsValidHeaderName(string name)
    {
        foreach (char character in name)
        {
            bool valid = char.IsAsciiLetterOrDigit(character)
                || character is
                    '!'
                    or '#'
                    or '$'
                    or '%'
                    or '&'
                    or '\''
                    or '*'
                    or '+'
                    or '-'
                    or '.'
                    or '^'
                    or '_'
                    or '`'
                    or '|'
                    or '~';
            if (!valid)
            {
                return false;
            }
        }

        return name.Length > 0;
    }

    private static bool IsValidHeaderValue(string value) =>
        value.All(static character =>
            character == '\t'
            || character is >= '\u0020' and < '\u007f'
            || character >= '\u0080');
}
