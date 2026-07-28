using System.Net;
using System.Text;
using LibraTray.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Protocol;

[TestClass]
public sealed class YeelightDiscoveryTests
{
    [TestMethod]
    public void CreateRequestMatchesOfficialWireBytesExactly()
    {
        const string Expected =
            "M-SEARCH * HTTP/1.1\r\n"
            + "HOST: 239.255.255.250:1982\r\n"
            + "MAN: \"ssdp:discover\"\r\n"
            + "ST: wifi_bulb\r\n";

        CollectionAssert.AreEqual(
            Encoding.ASCII.GetBytes(Expected),
            YeelightDiscovery.CreateRequest());
    }

    [TestMethod]
    public void ParseResponseUsesActualPortFromLocationAndKeepsUnknownHeaders()
    {
        byte[] response = Encoding.UTF8.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "Location: yeelight://192.0.2.44:61234\r\n"
            + "id: 0x0000000000000001\r\n"
            + "model: lamp15\r\n"
            + "name: Reported name\r\n"
            + "x-future-capability: enabled\r\n"
            + "\r\n");

        YeelightDiscoveryResponse parsed = YeelightDiscovery.ParseResponse(response);

        Assert.AreEqual(IPAddress.Parse("192.0.2.44"), parsed.ControlEndPoint.Address);
        Assert.AreEqual(61234, parsed.ControlEndPoint.Port);
        Assert.AreEqual("lamp15", parsed.Model);
        Assert.AreEqual("Reported name", parsed.ReportedName);
        Assert.AreEqual("enabled", parsed.Headers["X-Future-Capability"]);
    }

    [TestMethod]
    public void ParseResponseStatusPrefixThatIsNotExactly200IsRejected()
    {
        byte[] response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200evil\r\n"
            + "Location: yeelight://192.0.2.44:55443\r\n");

        Assert.ThrowsExactly<YeelightProtocolException>(
            () => YeelightDiscovery.ParseResponse(response));
    }

    [TestMethod]
    public void TryParseResponseMissingOrInvalidLocationReturnsFalse()
    {
        byte[] response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "Location: yeelight://192.0.2.44\r\n");

        bool parsed = YeelightDiscovery.TryParseResponse(response, out var result);

        Assert.IsFalse(parsed);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseResponseDuplicateLocationIsRejected()
    {
        byte[] response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "Location: yeelight://192.0.2.44:55443\r\n"
            + "LOCATION: yeelight://192.0.2.45:55443\r\n");

        Assert.ThrowsExactly<YeelightProtocolException>(
            () => YeelightDiscovery.ParseResponse(response));
    }
}
