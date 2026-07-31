using System.Globalization;

namespace LibraTray.Probe;

internal sealed class ProbeOptions
{
    internal const int MaximumProperties = 64;

    private static readonly HashSet<string> Commands =
    [
        "discover",
        "inspect",
        "get-props",
        "listen",
        "safe-write",
    ];

    private static readonly HashSet<string> BooleanOptions =
    [
        "confirm-write",
        "help",
        "no-redact",
    ];

    private static readonly HashSet<string> ValueOptions =
    [
        "discovery-port",
        "host",
        "listen-seconds",
        "local-address",
        "log-path",
        "method",
        "port",
        "props",
        "target",
        "timeout-seconds",
        "value",
    ];

    private readonly Dictionary<string, string?> _values;

    private ProbeOptions(string? command, Dictionary<string, string?> values)
    {
        Command = command;
        _values = values;
    }

    public string? Command { get; }

    public bool ShowHelp => Command is null || HasFlag("help");

    public string? Host => GetValue("host");

    public string? DiscoveryTarget => GetValue("target");

    public string? LocalAddress => GetValue("local-address");

    public int Port => GetInteger("port", 55_443, 1, 65_535);

    public bool HasExplicitPort => _values.ContainsKey("port");

    public int DiscoveryPort => GetInteger("discovery-port", 1_982, 1, 65_535);

    public TimeSpan Timeout =>
        TimeSpan.FromSeconds(GetInteger("timeout-seconds", 5, 1, 300));

    public TimeSpan? ListenDuration
    {
        get
        {
            int seconds = GetInteger("listen-seconds", 15, 0, 86_400);
            return seconds == 0 ? null : TimeSpan.FromSeconds(seconds);
        }
    }

    public IReadOnlyList<string> Properties
    {
        get
        {
            string raw = GetValue("props")
                ?? "power,bright,ct,rgb,hue,sat,color_mode,name,model,fw_ver,"
                    + "bg_power,bg_bright,bg_ct,bg_rgb,bg_hue,bg_sat,bg_lmode";

            string[] properties = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (properties.Length == 0)
            {
                throw new ArgumentException("--props 至少需要一个属性名。");
            }

            if (properties.Length > MaximumProperties)
            {
                throw new ArgumentException(
                    $"--props 最多允许 {MaximumProperties} 个属性名。");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string property in properties)
            {
                if (!IsValidProtocolIdentifier(property))
                {
                    throw new ArgumentException($"无效属性名：{property}");
                }

                if (DiagnosticRedactor.IsCredentialPropertyName(property))
                {
                    throw new ArgumentException(
                        $"拒绝读取凭据类属性：{property}。"
                        + "LibraTray 不读取或记录密码、Token。");
                }

                if (!seen.Add(property))
                {
                    throw new ArgumentException(
                        $"--props 包含重复属性名：{property}。");
                }
            }

            return properties;
        }
    }

    public string? SafeWriteMethod => GetValue("method");

    public string? SafeWriteValue => GetValue("value");

    public bool ConfirmWrite => HasFlag("confirm-write");

    public bool NoRedact => HasFlag("no-redact");

    public string? LogPath => GetValue("log-path");

    public static ProbeOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return new ProbeOptions(command: null, new Dictionary<string, string?>());
        }

        int index = 0;
        string? command = null;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!args[0].StartsWith("--", StringComparison.Ordinal))
        {
            command = args[0].ToLowerInvariant();
            if (!Commands.Contains(command))
            {
                throw new ArgumentException($"未知命令：{args[0]}");
            }

            index++;
        }

        while (index < args.Length)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
            {
                throw new ArgumentException($"无法识别的参数：{token}");
            }

            string option;
            string? value = null;
            int equalsIndex = token.IndexOf('=', StringComparison.Ordinal);

            if (equalsIndex >= 0)
            {
                option = token[2..equalsIndex];
                value = token[(equalsIndex + 1)..];
            }
            else
            {
                option = token[2..];
            }

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

            if (ValueOptions.Contains(option)
                && string.IsNullOrWhiteSpace(values[option]))
            {
                throw new ArgumentException($"选项 --{option} 的值不能为空。");
            }

            index++;
        }

        if (command is null && !values.ContainsKey("help"))
        {
            throw new ArgumentException("请指定命令。");
        }

        return new ProbeOptions(command, values);
    }

    private string? GetValue(string name) =>
        _values.TryGetValue(name, out string? value) ? value : null;

    private bool HasFlag(string name) => _values.ContainsKey(name);

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

    private static bool IsValidProtocolIdentifier(string value)
    {
        if (value.Length is 0 or > 128 || !IsAsciiLetter(value[0]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsAsciiLetter(character)
                && !char.IsAsciiDigit(character)
                && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}
