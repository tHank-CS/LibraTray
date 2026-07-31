using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LibraTray.Core.Protocol;

namespace LibraTray.Core.Networking;

public sealed record YeelightDiscoveredDevice(
    IPEndPoint Sender,
    YeelightDiscoveryResponse Response);

public sealed record YeelightDiscoveryOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public IPAddress? LocalAddress { get; init; }

    public IPEndPoint Target { get; init; } = new(
        IPAddress.Parse(YeelightDiscovery.MulticastAddress),
        YeelightDiscovery.MulticastPort);
}

/// <summary>
/// Discovers Yeelight LAN devices through bounded UDP requests. Responses are
/// accepted only when their sender and advertised control address are the same
/// loopback, RFC1918, or IPv4 link-local address.
/// </summary>
public static class YeelightDiscoveryClient
{
    private const int MaximumRecords = 64;
    private const int MaximumProcessedDatagrams = 512;
    private const int MaximumDatagramBytes = 16 * 1024;
    private const long MaximumProcessedBytes = 4L * 1024 * 1024;

    public static async Task<IReadOnlyList<YeelightDiscoveredDevice>> DiscoverAsync(
        YeelightDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        YeelightDiscoveryOptions effectiveOptions =
            options ?? new YeelightDiscoveryOptions();
        ValidateOptions(effectiveOptions);

        bool explicitAddress = effectiveOptions.LocalAddress is not null;
        IReadOnlyList<IPAddress> localAddresses = explicitAddress
            ? [effectiveOptions.LocalAddress!]
            : GetCandidateLocalAddresses();

        Task<IReadOnlyList<YeelightDiscoveredDevice>>[] tasks = localAddresses
            .Select(address => DiscoverFromAddressAsync(
                address,
                effectiveOptions.Target,
                effectiveOptions.Timeout,
                suppressSocketErrors: !explicitAddress,
                cancellationToken))
            .ToArray();

        IReadOnlyList<YeelightDiscoveredDevice>[] results =
            await Task.WhenAll(tasks).ConfigureAwait(false);

        var devices = new List<YeelightDiscoveredDevice>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (YeelightDiscoveredDevice device in results.SelectMany(static value => value))
        {
            string endpoint = device.Response.ControlEndPoint.ToString();
            string? id = device.Response.Id;
            if (seenEndpoints.Contains(endpoint)
                || (id is not null && seenIds.Contains(id)))
            {
                continue;
            }

            seenEndpoints.Add(endpoint);
            if (id is not null)
            {
                seenIds.Add(id);
            }

            devices.Add(device);
            if (devices.Count >= MaximumRecords)
            {
                break;
            }
        }

        return devices;
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

    private static async Task<IReadOnlyList<YeelightDiscoveredDevice>>
        DiscoverFromAddressAsync(
            IPAddress localAddress,
            IPEndPoint target,
            TimeSpan timeout,
            bool suppressSocketErrors,
            CancellationToken cancellationToken)
    {
        try
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.Bind(new IPEndPoint(localAddress, 0));
            if (IsYeelightMulticast(target.Address)
                && !localAddress.Equals(IPAddress.Any))
            {
                client.Client.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.MulticastInterface,
                    localAddress.GetAddressBytes());
            }

            byte[] request = YeelightDiscovery.CreateRequest();
            await client
                .SendAsync(request, target, cancellationToken)
                .ConfigureAwait(false);

            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCancellation.Token);
            var devices = new List<YeelightDiscoveredDevice>();
            int processedDatagrams = 0;
            long processedBytes = 0;

            try
            {
                while (devices.Count < MaximumRecords)
                {
                    UdpReceiveResult datagram = await client
                        .ReceiveAsync(operationCancellation.Token)
                        .ConfigureAwait(false);
                    processedDatagrams++;
                    processedBytes += datagram.Buffer.Length;

                    if (processedDatagrams > MaximumProcessedDatagrams
                        || processedBytes > MaximumProcessedBytes)
                    {
                        break;
                    }

                    if (datagram.Buffer.Length > MaximumDatagramBytes
                        || !YeelightDiscovery.TryParseResponse(
                            datagram.Buffer,
                            out YeelightDiscoveryResponse? response)
                        || response is null
                        || !IsAllowedLocalAddress(datagram.RemoteEndPoint.Address)
                        || !IsAllowedLocalAddress(response.ControlEndPoint.Address)
                        || !datagram.RemoteEndPoint.Address.Equals(
                            response.ControlEndPoint.Address))
                    {
                        continue;
                    }

                    devices.Add(new YeelightDiscoveredDevice(
                        datagram.RemoteEndPoint,
                        response));
                }
            }
            catch (OperationCanceledException) when (
                timeoutCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                // A completed timeout window is the normal discovery result.
            }

            return devices;
        }
        catch (SocketException) when (suppressSocketErrors)
        {
            return [];
        }
    }

    private static IPAddress[] GetCandidateLocalAddresses()
    {
        var addresses = new HashSet<IPAddress>();

        foreach (NetworkInterface networkInterface
                 in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType
                    is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation unicast
                     in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (IsAllowedLocalAddress(unicast.Address)
                    && !IPAddress.IsLoopback(unicast.Address))
                {
                    addresses.Add(unicast.Address);
                }
            }
        }

        return addresses.Count > 0 ? addresses.ToArray() : [IPAddress.Any];
    }

    private static void ValidateOptions(YeelightDiscoveryOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero
            || options.Timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Discovery timeout must be finite and positive.");
        }

        ArgumentNullException.ThrowIfNull(options.Target);
        if (options.Target.AddressFamily != AddressFamily.InterNetwork
            || options.Target.Port is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort
            || (!IsYeelightMulticast(options.Target.Address)
                && !IsAllowedLocalAddress(options.Target.Address)))
        {
            throw new ArgumentException(
                "Discovery target must be the Yeelight multicast endpoint or a local IPv4 endpoint.",
                nameof(options));
        }

        if (options.LocalAddress is not null
            && !IsAllowedLocalAddress(options.LocalAddress))
        {
            throw new ArgumentException(
                "Local discovery address must be loopback, RFC1918, or IPv4 link-local.",
                nameof(options));
        }
    }

    private static bool IsYeelightMulticast(IPAddress address) =>
        address.Equals(IPAddress.Parse(YeelightDiscovery.MulticastAddress));
}
