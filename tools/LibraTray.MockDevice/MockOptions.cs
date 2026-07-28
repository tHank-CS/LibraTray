using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace LibraTray.MockDevice;

internal enum MockMode
{
    Normal,
    Delay,
    Error,
    NoResponse,
    Split,
    Coalesce,
    Props,
    Disconnect,
    Restart,
    BadProps,
}

internal sealed class MockOptions
{
    private static readonly HashSet<string> BooleanOptions =
    [
        "help",
    ];

    private static readonly HashSet<string> ValueOptions =
    [
        "delay-ms",
        "discovery-port",
        "duration-seconds",
        "listen-address",
        "mode",
        "model",
        "name",
        "tcp-port",
    ];

    private readonly Dictionary<string, string?> _values;

    private MockOptions(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public bool ShowHelp => _values.ContainsKey("help");

    public MockMode Mode
    {
        get
        {
            string raw = GetValue("mode") ?? "normal";
            return raw.ToLowerInvariant() switch
            {
                "normal" => MockMode.Normal,
                "delay" => MockMode.Delay,
                "error" => MockMode.Error,
                "no-response" => MockMode.NoResponse,
                "split" => MockMode.Split,
                "coalesce" => MockMode.Coalesce,
                "props" => MockMode.Props,
                "disconnect" => MockMode.Disconnect,
                "restart" => MockMode.Restart,
                "bad-props" or "incorrect-props" => MockMode.BadProps,
                _ => throw new ArgumentException($"未知模拟模式：{raw}"),
            };
        }
    }

    public IPAddress ListenAddress
    {
        get
        {
            string raw = GetValue("listen-address") ?? IPAddress.Loopback.ToString();
            if (!IPAddress.TryParse(raw, out IPAddress? address)
                || address.AddressFamily != AddressFamily.InterNetwork
                || !IPAddress.IsLoopback(address))
            {
                throw new ArgumentException(
                    "--listen-address 仅允许 IPv4 loopback 地址（127.0.0.0/8）。");
            }

            return address;
        }
    }

    public int TcpPort => GetInteger("tcp-port", 55_443, 1, 65_535);

    public int DiscoveryPort => GetInteger("discovery-port", 1_982, 1, 65_535);

    public TimeSpan Delay =>
        TimeSpan.FromMilliseconds(GetInteger("delay-ms", 1_500, 0, 60_000));

    public TimeSpan? Duration
    {
        get
        {
            int seconds = GetInteger("duration-seconds", 0, 0, 86_400);
            return seconds == 0 ? null : TimeSpan.FromSeconds(seconds);
        }
    }

    public string Model => ValidateHeaderValue(
        GetValue("model") ?? "lamp15",
        "--model");

    public string Name => ValidateHeaderValue(
        GetValue("name") ?? "Mock Libra Pro",
        "--name");

    public static MockOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        int index = 0;

        while (index < args.Length)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
            {
                throw new ArgumentException($"无法识别的参数：{token}");
            }

            int equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
            string option = equalsIndex >= 0
                ? token[2..equalsIndex]
                : token[2..];
            string? value = equalsIndex >= 0
                ? token[(equalsIndex + 1)..]
                : null;

            if (!BooleanOptions.Contains(option) && !ValueOptions.Contains(option))
            {
                throw new ArgumentException($"未知选项：--{option}");
            }

            if (!values.TryAdd(option, value))
            {
                throw new ArgumentException($"选项重复：--{option}");
            }

            if (BooleanOptions.Contains(option))
            {
                if (value is not null)
                {
                    throw new ArgumentException($"布尔选项 --{option} 不接受值。");
                }
            }
            else if (value is null)
            {
                index++;
                if (index >= args.Length
                    || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"选项 --{option} 缺少值。");
                }

                values[option] = args[index];
            }

            index++;
        }

        return new MockOptions(values);
    }

    private string? GetValue(string name) =>
        _values.TryGetValue(name, out string? value) ? value : null;

    private int GetInteger(string name, int defaultValue, int minimum, int maximum)
    {
        string? raw = GetValue(name);
        if (raw is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(
                raw,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int value)
            || value < minimum
            || value > maximum)
        {
            throw new ArgumentException(
                $"--{name} 必须是 {minimum.ToString(CultureInfo.InvariantCulture)}"
                + $" 到 {maximum.ToString(CultureInfo.InvariantCulture)} 之间的整数。");
        }

        return value;
    }

    private static string ValidateHeaderValue(string value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || value.Any(char.IsControl)
            || value.Contains('\r', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{optionName} 必须是 1 到 128 个字符，且不能包含控制字符。");
        }

        return value;
    }
}
