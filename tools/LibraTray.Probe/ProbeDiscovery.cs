using System.Net;
using System.Net.Sockets;
using System.Text;
using LibraTray.Core.Protocol;

namespace LibraTray.Probe;

internal sealed record DiscoveryRecord(
    IPEndPoint Sender,
    YeelightDiscoveryResponse Response);

internal static class ProbeDiscovery
{
    internal const int MaximumDiscoveryRecords = 64;
    internal const int MaximumProcessedDatagrams = 512;
    internal const int MaximumProbeDatagramBytes = 16 * 1024;
    internal const long MaximumProcessedBytes = 4L * 1024 * 1024;

    public static async Task<IReadOnlyList<DiscoveryRecord>> DiscoverAsync(
        string target,
        int port,
        TimeSpan timeout,
        ProbeOutput output,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(output);

        IPAddress targetAddress = await ResolveIpv4Async(
            target,
            cancellationToken,
            allowDiscoveryMulticast: true).ConfigureAwait(false);
        var targetEndPoint = new IPEndPoint(targetAddress, port);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        byte[] request = YeelightDiscovery.CreateRequest();
        string requestText = Encoding.ASCII
            .GetString(request)
            .TrimEnd('\r', '\n');
        await output.SentPayloadAsync(
            "UDP",
            requestText,
            cancellationToken).ConfigureAwait(false);

        await client
            .SendAsync(request, targetEndPoint, cancellationToken)
            .ConfigureAwait(false);

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);

        var records = new List<DiscoveryRecord>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenControlEndpoints = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        int processedDatagrams = 0;
        long processedBytes = 0;

        try
        {
            while (true)
            {
                UdpReceiveResult datagram = await client
                    .ReceiveAsync(operationCancellation.Token)
                    .ConfigureAwait(false);
                processedDatagrams++;
                processedBytes += datagram.Buffer.Length;
                if (processedDatagrams > MaximumProcessedDatagrams
                    || processedBytes > MaximumProcessedBytes)
                {
                    await output.WarningAsync(
                        "discovery 已达到处理预算上限，停止接收更多 UDP 数据报。",
                        cancellationToken).ConfigureAwait(false);
                    return records;
                }

                if (datagram.Buffer.Length > MaximumProbeDatagramBytes)
                {
                    await output.WarningAsync(
                        $"忽略来自 {datagram.RemoteEndPoint} 的超大 discovery 响应。",
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!YeelightDiscovery.TryParseResponse(
                        datagram.Buffer,
                        out YeelightDiscoveryResponse? response)
                    || response is null)
                {
                    string malformedLog =
                        $"[REDACTED-MALFORMED-DATAGRAM bytes={datagram.Buffer.Length}]";
                    await output.RawResponseAsync(
                        "UDP",
                        malformedLog,
                        cancellationToken).ConfigureAwait(false);
                    await output.WarningAsync(
                        $"忽略来自 {datagram.RemoteEndPoint} 的无效 discovery 响应。",
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                string raw = Encoding.UTF8.GetString(datagram.Buffer);
                await output.RawResponseAsync(
                    "UDP",
                    raw,
                    cancellationToken).ConfigureAwait(false);

                if (!IsAllowedLocalAddress(datagram.RemoteEndPoint.Address)
                    || !IsAllowedLocalAddress(response.ControlEndPoint.Address))
                {
                    await output.WarningAsync(
                        "忽略指向非本地网络地址的 discovery 响应；"
                        + "探针只允许 loopback、RFC1918 与 IPv4 link-local 端点。",
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!datagram.RemoteEndPoint.Address.Equals(
                        response.ControlEndPoint.Address))
                {
                    await output.WarningAsync(
                        "忽略 discovery 响应：UDP 发送端地址与 Location 控制端点地址不一致。",
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                string endpointKey = response.ControlEndPoint.ToString();
                if (seenControlEndpoints.Contains(endpointKey)
                    || (response.Id is not null && seenIds.Contains(response.Id)))
                {
                    continue;
                }

                seenControlEndpoints.Add(endpointKey);
                if (response.Id is not null)
                {
                    seenIds.Add(response.Id);
                }

                var record = new DiscoveryRecord(
                    datagram.RemoteEndPoint,
                    response);
                records.Add(record);
                await output.DiscoveryAsync(
                    record.Sender,
                    record.Response,
                    cancellationToken).ConfigureAwait(false);

                if (records.Count >= MaximumDiscoveryRecords)
                {
                    await output.WarningAsync(
                        $"discovery 已达到 {MaximumDiscoveryRecords} 条响应上限，停止接收。",
                        cancellationToken).ConfigureAwait(false);
                    return records;
                }
            }
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return records;
        }
    }

    public static async Task<IPAddress> ResolveIpv4Async(
        string host,
        CancellationToken cancellationToken,
        bool allowDiscoveryMulticast = false)
    {
        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            if (literal.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "当前协议探针的 UDP discovery 仅支持 IPv4 目标。",
                    nameof(host));
            }

            if (IsAllowedLocalAddress(literal)
                || (allowDiscoveryMulticast && IsYeelightDiscoveryMulticast(literal)))
            {
                return literal;
            }

            throw new ArgumentException(
                "拒绝非本地网络目标：仅允许 loopback、RFC1918、IPv4 link-local，"
                + "discovery 另允许 239.255.255.250。",
                nameof(host));
        }

        IPAddress[] addresses = await Dns
            .GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken)
            .ConfigureAwait(false);

        IPAddress? localAddress = addresses.FirstOrDefault(IsAllowedLocalAddress);
        if (localAddress is not null)
        {
            return localAddress;
        }

        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        throw new ArgumentException(
            "主机名没有解析到允许的本地网络 IPv4 地址。",
            nameof(host));
    }

    public static bool IsAllowedLocalAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool IsYeelightDiscoveryMulticast(IPAddress address) =>
        address.Equals(IPAddress.Parse(YeelightDiscovery.MulticastAddress));
}
