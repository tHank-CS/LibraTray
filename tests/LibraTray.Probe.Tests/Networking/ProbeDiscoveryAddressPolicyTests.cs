using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Probe.Tests.Networking;

[TestClass]
public sealed class ProbeDiscoveryAddressPolicyTests
{
    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("127.255.255.254")]
    [DataRow("10.0.0.0")]
    [DataRow("10.255.255.255")]
    [DataRow("172.16.0.0")]
    [DataRow("172.31.255.255")]
    [DataRow("192.168.0.0")]
    [DataRow("192.168.255.255")]
    [DataRow("169.254.0.0")]
    [DataRow("169.254.255.255")]
    public void IsAllowedLocalAddressAcceptsOnlyDocumentedIpv4LocalRanges(string value)
    {
        Assert.IsTrue(ProbeDiscovery.IsAllowedLocalAddress(IPAddress.Parse(value)));
    }

    [TestMethod]
    [DataRow("0.0.0.0")]
    [DataRow("9.255.255.255")]
    [DataRow("11.0.0.0")]
    [DataRow("169.253.255.255")]
    [DataRow("169.255.0.0")]
    [DataRow("172.15.255.255")]
    [DataRow("172.32.0.0")]
    [DataRow("192.167.255.255")]
    [DataRow("192.169.0.0")]
    [DataRow("224.0.0.1")]
    [DataRow("239.255.255.250")]
    [DataRow("255.255.255.255")]
    [DataRow("::1")]
    [DataRow("fe80::1")]
    public void IsAllowedLocalAddressRejectsAddressesOutsideIpv4LocalRanges(string value)
    {
        Assert.IsFalse(ProbeDiscovery.IsAllowedLocalAddress(IPAddress.Parse(value)));
    }

    [TestMethod]
    public async Task ResolveIpv4AllowsOnlyExactYeelightDiscoveryMulticastWhenOptedIn()
    {
        const string YeelightMulticast = "239.255.255.250";

        IPAddress result = await ProbeDiscovery.ResolveIpv4Async(
            YeelightMulticast,
            CancellationToken.None,
            allowDiscoveryMulticast: true);

        Assert.AreEqual(IPAddress.Parse(YeelightMulticast), result);
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => ProbeDiscovery.ResolveIpv4Async(
                YeelightMulticast,
                CancellationToken.None));
    }

    [TestMethod]
    [DataRow("224.0.0.1")]
    [DataRow("239.255.255.249")]
    [DataRow("239.255.255.251")]
    [DataRow("239.255.255.255")]
    public async Task ResolveIpv4RejectsEveryOtherMulticastAddress(string value)
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => ProbeDiscovery.ResolveIpv4Async(
                value,
                CancellationToken.None,
                allowDiscoveryMulticast: true));
    }

    [TestMethod]
    [DataRow("::1")]
    [DataRow("fe80::1")]
    public async Task ResolveIpv4RejectsIpv6Literals(string value)
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => ProbeDiscovery.ResolveIpv4Async(
                value,
                CancellationToken.None,
                allowDiscoveryMulticast: true));
    }

    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("10.1.2.3")]
    [DataRow("172.16.0.1")]
    [DataRow("192.168.1.20")]
    [DataRow("169.254.1.2")]
    public void ParseLocalBindAddressAcceptsDocumentedLocalIpv4Ranges(string value)
    {
        Assert.AreEqual(
            IPAddress.Parse(value),
            ProbeDiscovery.ParseLocalBindAddress(value));
    }

    [TestMethod]
    [DataRow("0.0.0.0")]
    [DataRow("8.8.8.8")]
    [DataRow("239.255.255.250")]
    [DataRow("::1")]
    [DataRow("localhost")]
    public void ParseLocalBindAddressRejectsNonLocalOrNonLiteralValues(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ProbeDiscovery.ParseLocalBindAddress(value));
    }
}
