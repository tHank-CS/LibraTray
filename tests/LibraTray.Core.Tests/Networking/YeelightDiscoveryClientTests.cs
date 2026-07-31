using System.Net;
using System.Net.Sockets;
using System.Text;
using LibraTray.Core.Networking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Networking;

[TestClass]
public sealed class YeelightDiscoveryClientTests
{
    [TestMethod]
    public async Task DiscoverAcceptsExactLocalSenderAndControlAddress()
    {
        using var responder = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        int discoveryPort = ((IPEndPoint)responder.Client.LocalEndPoint!).Port;
        Task responseTask = RespondOnceAsync(
            responder,
            "127.0.0.1",
            model: "lamp15");
        IReadOnlyList<YeelightDiscoveredDevice> devices =
            await YeelightDiscoveryClient.DiscoverAsync(
                new YeelightDiscoveryOptions
                {
                    LocalAddress = IPAddress.Loopback,
                    Target = new IPEndPoint(
                        IPAddress.Loopback,
                        discoveryPort),
                    Timeout = TimeSpan.FromMilliseconds(250),
                });
        await responseTask;

        Assert.HasCount(1, devices);
        Assert.AreEqual("lamp15", devices[0].Response.Model);
        Assert.AreEqual(
            new IPEndPoint(IPAddress.Loopback, 55_443),
            devices[0].Response.ControlEndPoint);
    }

    [TestMethod]
    public async Task DiscoverRejectsSenderAndControlAddressMismatch()
    {
        using var responder = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        int discoveryPort = ((IPEndPoint)responder.Client.LocalEndPoint!).Port;
        Task responseTask = RespondOnceAsync(
            responder,
            "192.168.50.20",
            model: "lamp15");
        IReadOnlyList<YeelightDiscoveredDevice> devices =
            await YeelightDiscoveryClient.DiscoverAsync(
                new YeelightDiscoveryOptions
                {
                    LocalAddress = IPAddress.Loopback,
                    Target = new IPEndPoint(
                        IPAddress.Loopback,
                        discoveryPort),
                    Timeout = TimeSpan.FromMilliseconds(250),
                });
        await responseTask;

        Assert.HasCount(0, devices);
    }

    [TestMethod]
    public async Task DiscoverRejectsPublicTargetBeforeOpeningSocket()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => YeelightDiscoveryClient.DiscoverAsync(
                new YeelightDiscoveryOptions
                {
                    Target = new IPEndPoint(
                        IPAddress.Parse("8.8.8.8"),
                        1_982),
                }));
    }

    private static async Task RespondOnceAsync(
        UdpClient responder,
        string controlAddress,
        string model)
    {
        UdpReceiveResult request = await responder.ReceiveAsync();
        string response =
            "HTTP/1.1 200 OK\r\n"
            + $"Location: yeelight://{controlAddress}:55443\r\n"
            + "id: 0x0000000000000001\r\n"
            + $"model: {model}\r\n"
            + "support: get_prop set_power bg_set_power\r\n"
            + "\r\n";
        byte[] payload = Encoding.UTF8.GetBytes(response);
        _ = await responder.SendAsync(payload, request.RemoteEndPoint);
    }
}
