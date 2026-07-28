using System.Globalization;
using System.Net;
using System.Net.Sockets;
using LibraTray.Core.Identity;
using LibraTray.Core.Networking;
using LibraTray.Core.Protocol;

namespace LibraTray.Probe;

internal static class ProbeApplication
{
    private const string Title = "Yeelight Libra Pro Protocol Probe";
    private const int MaximumGetPropProperties = 15;

    public static async Task<int> RunAsync(string[] args)
    {
        Console.WriteLine(Title);

        ProbeOptions options;
        try
        {
            options = ProbeOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"参数错误：{exception.Message}");
            PrintHelp();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        DiagnosticLogger logger;
        try
        {
            logger = DiagnosticLogger.Create(options.LogPath, redact: !options.NoRedact);
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"无法创建诊断日志：{exception.Message}");
            return 3;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"无权创建诊断日志：{exception.Message}");
            return 3;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"诊断日志路径无效：{exception.Message}");
            return 2;
        }

        await using (logger.ConfigureAwait(false))
        {
            var output = new ProbeOutput(logger);
            output.RegisterConnectionTarget(options.Host);
            output.RegisterConnectionTarget(options.DiscoveryTarget);
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                await output.InfoAsync(
                    $"诊断日志：{logger.Path}",
                    cancellation.Token).ConfigureAwait(false);

                if (options.NoRedact)
                {
                    const string warning =
                        "!!! 强警告：--no-redact 已启用。日志可能包含 IP、MAC、设备 ID、"
                        + "主机名、用户名和绝对路径；分享前必须人工审查。!!!";
                    Console.Error.WriteLine(warning);
                    await output.WarningAsync(
                        warning,
                        cancellation.Token).ConfigureAwait(false);
                }

                return await RunCommandAsync(
                    options,
                    output,
                    cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Console.Error.WriteLine("操作已由 Ctrl+C 安全取消。");
                return 130;
            }
            catch (TimeoutException exception)
            {
                await output.ErrorAsync(exception.Message).ConfigureAwait(false);
                return 3;
            }
            catch (ProbeCommandRejectedException exception)
            {
                await output.ErrorAsync(exception.Message).ConfigureAwait(false);
                return 5;
            }
            catch (YeelightDisconnectedException exception)
            {
                await output.ErrorAsync($"设备离线或连接中断：{exception.Message}")
                    .ConfigureAwait(false);
                return 3;
            }
            catch (SocketException exception)
            {
                await output.ErrorAsync(
                    $"网络错误 ({exception.SocketErrorCode})：{exception.Message}")
                    .ConfigureAwait(false);
                return 3;
            }
            catch (IOException exception)
            {
                await output.ErrorAsync($"I/O 错误：{exception.Message}")
                    .ConfigureAwait(false);
                return 3;
            }
            catch (YeelightProtocolException exception)
            {
                await output.ErrorAsync($"协议错误：{exception.Message}")
                    .ConfigureAwait(false);
                return 3;
            }
            catch (ArgumentException exception)
            {
                await output.ErrorAsync($"参数错误：{exception.Message}")
                    .ConfigureAwait(false);
                return 2;
            }
            catch (InvalidOperationException exception)
            {
                await output.ErrorAsync($"操作无法完成：{exception.Message}")
                    .ConfigureAwait(false);
                return 3;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    private static Task<int> RunCommandAsync(
        ProbeOptions options,
        ProbeOutput output,
        CancellationToken cancellationToken) =>
        options.Command switch
        {
            "discover" => RunDiscoverAsync(options, output, cancellationToken),
            "inspect" => RunInspectAsync(options, output, cancellationToken),
            "get-props" => RunGetPropertiesAsync(options, output, cancellationToken),
            "listen" => RunListenAsync(options, output, cancellationToken),
            "safe-write" => RunSafeWriteAsync(options, output, cancellationToken),
            _ => throw new ArgumentException("请指定有效命令。"),
        };

    private static async Task<int> RunDiscoverAsync(
        ProbeOptions options,
        ProbeOutput output,
        CancellationToken cancellationToken)
    {
        string target = options.DiscoveryTarget
            ?? YeelightDiscovery.MulticastAddress;
        IReadOnlyList<DiscoveryRecord> records = await ProbeDiscovery.DiscoverAsync(
            target,
            options.DiscoveryPort,
            options.Timeout,
            output,
            cancellationToken).ConfigureAwait(false);

        foreach (DiscoveryRecord record in records)
        {
            DeviceIdentity identity = ProductIdentityMapper.Resolve(
                record.Response.Model,
                record.Response.ReportedName);
            await output.IdentityAsync(identity, cancellationToken).ConfigureAwait(false);
        }

        if (records.Count == 0)
        {
            await output.WarningAsync(
                "超时窗口内未发现设备；未发送任何控制命令。",
                cancellationToken).ConfigureAwait(false);
            return 4;
        }

        await output.InfoAsync(
            $"发现 {records.Count.ToString(CultureInfo.InvariantCulture)} 台设备。",
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunInspectAsync(
        ProbeOptions options,
        ProbeOutput output,
        CancellationToken cancellationToken)
    {
        string discoveryTarget = options.DiscoveryTarget
            ?? options.Host
            ?? YeelightDiscovery.MulticastAddress;
        IReadOnlyList<DiscoveryRecord> records = await ProbeDiscovery.DiscoverAsync(
            discoveryTarget,
            options.DiscoveryPort,
            options.Timeout,
            output,
            cancellationToken).ConfigureAwait(false);

        if (options.Host is not null)
        {
            IPAddress hostAddress = await ProbeDiscovery.ResolveIpv4Async(
                options.Host,
                cancellationToken).ConfigureAwait(false);
            DiscoveryRecord? record = FindDiscoveryRecord(
                records,
                hostAddress,
                options.HasExplicitPort ? options.Port : null);
            string tcpHost = record is not null && !options.HasExplicitPort
                ? record.Response.ControlEndPoint.Address.ToString()
                : hostAddress.ToString();
            int tcpPort = record is not null && !options.HasExplicitPort
                ? record.Response.ControlEndPoint.Port
                : options.Port;

            await InspectEndpointAsync(
                tcpHost,
                tcpPort,
                record,
                options,
                output,
                cancellationToken).ConfigureAwait(false);
            return 0;
        }

        if (records.Count == 0)
        {
            await output.WarningAsync(
                "未发现可检查的设备。",
                cancellationToken).ConfigureAwait(false);
            return 4;
        }

        int failedEndpoints = 0;
        foreach (DiscoveryRecord record in records)
        {
            try
            {
                await InspectEndpointAsync(
                    record.Response.ControlEndPoint.Address.ToString(),
                    record.Response.ControlEndPoint.Port,
                    record,
                    options,
                    output,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsEndpointInspectionFailure(exception))
            {
                failedEndpoints++;
                await output.ErrorAsync(
                    $"检查 {record.Response.ControlEndPoint} 失败，继续检查其他设备："
                    + exception.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return failedEndpoints == 0 ? 0 : 3;
    }

    private static async Task InspectEndpointAsync(
        string host,
        int port,
        DiscoveryRecord? discovery,
        ProbeOptions options,
        ProbeOutput output,
        CancellationToken cancellationToken)
    {
        if (discovery is not null)
        {
            await output.IdentityAsync(
                ProductIdentityMapper.Resolve(
                    discovery.Response.Model,
                    discovery.Response.ReportedName),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await output.WarningAsync(
                "未收到 discovery 身份信息；将只读查询指定 TCP 端点。",
                cancellationToken).ConfigureAwait(false);
        }

        await using var session = new ProbeTcpSession(output);
        await session.ConnectAsync(
            host,
            port,
            options.Timeout,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> properties = await QueryPropertiesAsync(
            session,
            options.Properties,
            options.Timeout,
            output,
            cancellationToken).ConfigureAwait(false);

        if (discovery is null)
        {
            properties.TryGetValue("model", out string? model);
            properties.TryGetValue("name", out string? reportedName);
            await output.IdentityAsync(
                ProductIdentityMapper.Resolve(model, reportedName),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> RunGetPropertiesAsync(
        ProbeOptions options,
        ProbeOutput output,
        CancellationToken cancellationToken)
    {
        string host = RequireHost(options);
        await using var session = new ProbeTcpSession(output);
        await session.ConnectAsync(
            host,
            options.Port,
            options.Timeout,
            cancellationToken).ConfigureAwait(false);
        await QueryPropertiesAsync(
            session,
            options.Properties,
            options.Timeout,
            output,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunListenAsync(
        ProbeOptions options,
        ProbeOutput output,
        CancellationToken cancellationToken)
    {
        string host = RequireHost(options);
        await using var session = new ProbeTcpSession(output);
        await session.ConnectAsync(
            host,
            options.Port,
            options.Timeout,
            cancellationToken).ConfigureAwait(false);

        string duration = options.ListenDuration is null
            ? "直到 Ctrl+C"
            : $"{options.ListenDuration.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture)} 秒";
        await output.InfoAsync(
            $"开始监听 props/通知（{duration}）。",
            cancellationToken).ConfigureAwait(false);
        await session.ListenAsync(
            options.ListenDuration,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunSafeWriteAsync(
        ProbeOptions options,
        ProbeOutput output,
        CancellationToken cancellationToken)
    {
        string host = RequireHost(options);
        if (!options.ConfirmWrite)
        {
            await output.ErrorAsync(
                "拒绝写入：必须显式提供 --confirm-write。",
                cancellationToken).ConfigureAwait(false);
            return 5;
        }

        SafeWriteSpec write = ParseSafeWrite(options);
        string discoveryTarget = options.DiscoveryTarget ?? host;
        IReadOnlyList<DiscoveryRecord> records = await ProbeDiscovery.DiscoverAsync(
            discoveryTarget,
            options.DiscoveryPort,
            options.Timeout,
            output,
            cancellationToken).ConfigureAwait(false);

        IPAddress hostAddress = await ProbeDiscovery.ResolveIpv4Async(
            host,
            cancellationToken).ConfigureAwait(false);
        DiscoveryRecord? record = FindDiscoveryRecord(
            records,
            hostAddress,
            options.HasExplicitPort ? options.Port : null);

        if (record is null)
        {
            await output.ErrorAsync(
                "拒绝写入：未从目标 TCP IP:port 取得精确匹配的 discovery 能力声明。",
                cancellationToken).ConfigureAwait(false);
            return 5;
        }

        SafeWriteCapabilityResult capabilityResult = EvaluateSafeWriteCapabilities(
            record,
            write);
        if (capabilityResult == SafeWriteCapabilityResult.MissingWriteMethod)
        {
            await output.ErrorAsync(
                $"拒绝写入：设备 discovery 的 support 未声明 {write.Method}。",
                cancellationToken).ConfigureAwait(false);
            return 5;
        }

        if (capabilityResult == SafeWriteCapabilityResult.MissingGetProp)
        {
            await output.ErrorAsync(
                "拒绝写入：写前/写后验证需要 get_prop，"
                + "但设备 discovery 的 support 未声明该能力。",
                cancellationToken).ConfigureAwait(false);
            return 5;
        }

        await output.WarningAsync(
            $"写入已显式确认：仅执行官方通用命令 {write.Method}；"
            + "不会发送任何猜测的 lamp15 私有命令。",
            cancellationToken).ConfigureAwait(false);

        IPEndPoint controlEndPoint = record.Response.ControlEndPoint;
        string tcpHost = controlEndPoint.Address.ToString();
        int tcpPort = controlEndPoint.Port;

        await using var session = new ProbeTcpSession(output);
        await session.ConnectAsync(
            tcpHost,
            tcpPort,
            options.Timeout,
            cancellationToken).ConfigureAwait(false);

        await output.InfoAsync(
            $"写入前读取 {write.PropertyName}。",
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> beforeWrite = await QueryPropertiesAsync(
            session,
            [write.PropertyName],
            options.Timeout,
            output,
            cancellationToken).ConfigureAwait(false);

        if (!beforeWrite.TryGetValue(write.PropertyName, out string? currentValue)
            || !IsValidCurrentPropertyValue(write, currentValue))
        {
            await output.ErrorAsync(
                $"拒绝写入：写入前无法读取有效的 {write.PropertyName} 当前值"
                + $"（实际值：{currentValue ?? "<missing>"}）。",
                cancellationToken).ConfigureAwait(false);
            return 5;
        }

        try
        {
            await session.SendCommandAsync(
                write.Method,
                write.Parameters,
                options.Timeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await output.WarningAsync(
                "写入命令可能已经到达设备，但操作已取消；设备状态不确定，请重新读取。",
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (IsUncertainWriteTransportFailure(exception))
        {
            await output.ErrorAsync(
                "写入命令可能已经到达设备，但未获得可确认响应；"
                + $"设备状态不确定：{exception.Message}",
                cancellationToken).ConfigureAwait(false);
            return 6;
        }

        IReadOnlyDictionary<string, string> afterWrite;
        try
        {
            await output.InfoAsync(
                $"写入后复读 {write.PropertyName}。",
                cancellationToken).ConfigureAwait(false);
            afterWrite = await QueryPropertiesAsync(
                session,
                [write.PropertyName],
                options.Timeout,
                output,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await output.WarningAsync(
                "写入已获设备响应，但写后复读被取消；设备状态不确定，请重新读取。",
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (IsPostWriteVerificationFailure(exception))
        {
            await output.ErrorAsync(
                "写入已获设备响应，但写后复读失败；"
                + $"设备状态不确定：{exception.Message}",
                cancellationToken).ConfigureAwait(false);
            return 6;
        }

        if (!afterWrite.TryGetValue(write.PropertyName, out string? actualValue)
            || !string.Equals(
                actualValue,
                write.ExpectedPropertyValue,
                StringComparison.Ordinal))
        {
            await output.ErrorAsync(
                $"写后验证失败：{write.PropertyName} 期望值为 "
                + $"{write.ExpectedPropertyValue}，实际值为 {actualValue ?? "<missing>"}。",
                cancellationToken).ConfigureAwait(false);
            return 6;
        }

        await output.InfoAsync(
            $"写后验证成功：{write.PropertyName}={write.ExpectedPropertyValue}。",
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    internal static DiscoveryRecord? FindDiscoveryRecord(
        IReadOnlyList<DiscoveryRecord> records,
        IPAddress hostAddress,
        int? requiredPort)
    {
        DiscoveryRecord[] matches = records
            .Where(candidate =>
                candidate.Sender.Address.Equals(
                    candidate.Response.ControlEndPoint.Address)
                && candidate.Response.ControlEndPoint.Address.Equals(hostAddress)
                && (requiredPort is null
                    || candidate.Response.ControlEndPoint.Port == requiredPort.Value))
            .ToArray();

        if (requiredPort is null
            && matches
                .Select(candidate => candidate.Response.ControlEndPoint.Port)
                .Distinct()
                .Skip(1)
                .Any())
        {
            throw new InvalidOperationException(
                $"discovery 为 {hostAddress} 返回了多个 TCP 端口；"
                + "请用 --port 显式指定要检查的端点。");
        }

        return matches.FirstOrDefault();
    }

    internal static SafeWriteCapabilityResult EvaluateSafeWriteCapabilities(
        DiscoveryRecord record,
        SafeWriteSpec write)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(write);

        string capabilities = record.Response.Headers.TryGetValue(
            "support",
            out string? declared)
            ? declared
            : string.Empty;
        string[] declaredCapabilities = capabilities
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!declaredCapabilities.Contains(write.Method, StringComparer.Ordinal))
        {
            return SafeWriteCapabilityResult.MissingWriteMethod;
        }

        return declaredCapabilities.Contains("get_prop", StringComparer.Ordinal)
            ? SafeWriteCapabilityResult.Allowed
            : SafeWriteCapabilityResult.MissingGetProp;
    }

    internal static bool IsValidCurrentPropertyValue(
        SafeWriteSpec write,
        string? value)
    {
        ArgumentNullException.ThrowIfNull(write);

        return write.PropertyName switch
        {
            "power" or "bg_power" => value is "on" or "off",
            "bright" or "bg_bright" => IsIntegerInRange(value, 1, 100),
            "ct" => IsIntegerInRange(value, 1_700, 6_500),
            "rgb" or "bg_rgb" => IsIntegerInRange(value, 0, 16_777_215),
            _ => false,
        };
    }

    private static async Task<IReadOnlyDictionary<string, string>> QueryPropertiesAsync(
        ProbeTcpSession session,
        IReadOnlyList<string> properties,
        TimeSpan timeout,
        ProbeOutput output,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int offset = 0; offset < properties.Count; offset += MaximumGetPropProperties)
        {
            string[] batch = properties
                .Skip(offset)
                .Take(MaximumGetPropProperties)
                .ToArray();
            YeelightSuccessResponse response = await session.SendCommandAsync(
                "get_prop",
                batch.Cast<object?>(),
                timeout,
                cancellationToken).ConfigureAwait(false);

            var batchValues = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < batch.Length; index++)
            {
                string value = index < response.Results.Count
                    ? ProbeOutput.FormatJsonElement(response.Results[index])
                    : "<missing>";
                batchValues[batch[index]] = value;
                values[batch[index]] = value;
            }

            await output.PropertiesAsync(
                batchValues,
                cancellationToken).ConfigureAwait(false);
        }

        return values;
    }

    internal static SafeWriteSpec ParseSafeWrite(ProbeOptions options)
    {
        string method = options.SafeWriteMethod?.ToLowerInvariant()
            ?? throw new ArgumentException("safe-write 需要 --method。");
        string value = options.SafeWriteValue
            ?? throw new ArgumentException("safe-write 需要 --value。");

        return method switch
        {
            "set_power" when value is "on" or "off" => new SafeWriteSpec(
                method,
                "power",
                value,
                [value, "sudden", 0]),
            "set_power" => throw new ArgumentException(
                "set_power 的 --value 只能是 on 或 off。"),
            "set_bright" => CreateNumericWrite(
                method,
                "bright",
                value,
                minimum: 1,
                maximum: 100),
            "set_ct_abx" => CreateNumericWrite(
                method,
                "ct",
                value,
                minimum: 1_700,
                maximum: 6_500),
            "set_rgb" => CreateNumericWrite(
                method,
                "rgb",
                value,
                minimum: 0,
                maximum: 16_777_215),
            "bg_set_power" when value is "on" or "off" => new SafeWriteSpec(
                method,
                "bg_power",
                value,
                [value, "sudden", 0]),
            "bg_set_power" => throw new ArgumentException(
                "bg_set_power 的 --value 只能是 on 或 off。"),
            "bg_set_bright" => CreateNumericWrite(
                method,
                "bg_bright",
                value,
                minimum: 1,
                maximum: 100),
            "bg_set_rgb" => CreateNumericWrite(
                method,
                "bg_rgb",
                value,
                minimum: 0,
                maximum: 16_777_215),
            _ => throw new ArgumentException(
                "安全写白名单仅包含 set_power、set_bright、set_ct_abx、set_rgb、"
                + "bg_set_power、bg_set_bright、bg_set_rgb。"),
        };
    }

    private static SafeWriteSpec CreateNumericWrite(
        string method,
        string propertyName,
        string rawValue,
        int minimum,
        int maximum)
    {
        if (!int.TryParse(
                rawValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int value)
            || value < minimum
            || value > maximum)
        {
            throw new ArgumentException(
                $"{method} 的 --value 必须是 {minimum.ToString(CultureInfo.InvariantCulture)}"
                + $" 到 {maximum.ToString(CultureInfo.InvariantCulture)} 之间的整数。");
        }

        return new SafeWriteSpec(
            method,
            propertyName,
            value.ToString(CultureInfo.InvariantCulture),
            [value, "sudden", 0]);
    }

    private static bool IsIntegerInRange(string? rawValue, int minimum, int maximum) =>
        int.TryParse(
            rawValue,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int value)
        && value >= minimum
        && value <= maximum;

    private static bool IsUncertainWriteTransportFailure(Exception exception) =>
        exception is TimeoutException
            or YeelightDisconnectedException
            or SocketException
            or IOException
            or YeelightProtocolException;

    private static bool IsPostWriteVerificationFailure(Exception exception) =>
        IsUncertainWriteTransportFailure(exception)
        || exception is ProbeCommandRejectedException;

    private static bool IsEndpointInspectionFailure(Exception exception) =>
        IsPostWriteVerificationFailure(exception)
        || exception is InvalidOperationException;

    private static string RequireHost(ProbeOptions options) =>
        options.Host
        ?? throw new ArgumentException("此命令需要 --host <IPv4 或主机名>。");

    private static void PrintHelp()
    {
        Console.WriteLine(
            """

            用法：
              LibraTray.Probe discover [--target 239.255.255.250] [--discovery-port 1982]
              LibraTray.Probe inspect [--host HOST] [--port 55443]
              LibraTray.Probe get-props --host HOST [--port 55443] [--props power,bright,...]
              LibraTray.Probe listen --host HOST [--port 55443] [--listen-seconds 15]
              LibraTray.Probe safe-write --host HOST --method METHOD --value VALUE --confirm-write

            通用选项：
              --timeout-seconds N   连接、请求和 discovery 超时，默认 5 秒
              --log-path PATH       覆盖默认 JSONL 日志路径
              --no-redact           禁用日志脱敏（会显示强警告）
              --help                显示帮助

            安全边界：
              discover / inspect / get-props / listen 均不写设备状态。
              safe-write 只允许 set_power、set_bright、set_ct_abx、set_rgb；
              氛围灯逐项测试另允许 bg_set_power、bg_set_bright、bg_set_rgb；
              同时要求 --confirm-write，且设备 discovery support 必须明确声明该命令。
              写前/写后复读还要求设备明确声明 get_prop。
              本工具不实现任何猜测的 lamp15 私有、分区或专有命令。

            默认日志：
              %LOCALAPPDATA%\LibraTray\logs\protocol-probe-*.jsonl
              默认脱敏 IP、MAC、设备 ID、主机名、用户名与绝对路径。
            """);
    }

    internal sealed record SafeWriteSpec(
        string Method,
        string PropertyName,
        string ExpectedPropertyValue,
        IReadOnlyList<object?> Parameters);

    internal enum SafeWriteCapabilityResult
    {
        Allowed,
        MissingWriteMethod,
        MissingGetProp,
    }
}
