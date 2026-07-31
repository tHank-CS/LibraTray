using System.Net;
using System.Text;
using LibraTray.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Probe.Tests.Networking;

[TestClass]
public sealed class ProbeApplicationSafetyTests
{
    private static readonly string[] Lamp15PowerProperties =
        ["power", "main_power", "bg_power"];

    private static readonly string[] GenericPowerProperties = ["power"];

    [TestMethod]
    [DataRow("set_power", "on", "power", "on")]
    [DataRow("set_bright", "42", "bright", "42")]
    [DataRow("set_ct_abx", "4000", "ct", "4000")]
    [DataRow("set_rgb", "16711935", "rgb", "16711935")]
    [DataRow("bg_set_power", "off", "bg_power", "off")]
    [DataRow("bg_set_bright", "73", "bg_bright", "73")]
    [DataRow("bg_set_rgb", "255", "bg_rgb", "255")]
    public void ParseSafeWriteAllowsOnlyDocumentedGenericCommands(
        string method,
        string value,
        string propertyName,
        string expectedValue)
    {
        ProbeApplication.SafeWriteSpec write = ParseSafeWrite(method, value);

        Assert.AreEqual(method, write.Method);
        Assert.AreEqual(propertyName, write.PropertyName);
        Assert.AreEqual(expectedValue, write.ExpectedPropertyValue);
    }

    [TestMethod]
    [DataRow("set_power", "toggle")]
    [DataRow("set_bright", "0")]
    [DataRow("set_bright", "101")]
    [DataRow("set_ct_abx", "1699")]
    [DataRow("set_ct_abx", "6501")]
    [DataRow("set_rgb", "16777216")]
    [DataRow("bg_set_bright", "-1")]
    [DataRow("bg_set_rgb", "16777216")]
    [DataRow("set_scene", "1")]
    [DataRow("set_ps", "1")]
    public void ParseSafeWriteRejectsUnknownPrivateAndOutOfRangeCommands(
        string method,
        string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ParseSafeWrite(method, value));
    }

    [TestMethod]
    public void EvaluateSafeWriteCapabilitiesRequiresWriteMethodAndGetProp()
    {
        var address = IPAddress.Parse("192.0.2.30");
        ProbeApplication.SafeWriteSpec write = ParseSafeWrite("set_bright", "42");

        DiscoveryRecord missingWrite = CreateRecord(
            senderAddress: address,
            controlAddress: address,
            controlPort: 55_443,
            support: "get_prop");
        DiscoveryRecord missingGetProp = CreateRecord(
            senderAddress: address,
            controlAddress: address,
            controlPort: 55_443,
            support: "set_bright");
        DiscoveryRecord allowed = CreateRecord(
            senderAddress: address,
            controlAddress: address,
            controlPort: 55_443,
            support: "get_prop set_bright");

        Assert.AreEqual(
            ProbeApplication.SafeWriteCapabilityResult.MissingWriteMethod,
            ProbeApplication.EvaluateSafeWriteCapabilities(missingWrite, write));
        Assert.AreEqual(
            ProbeApplication.SafeWriteCapabilityResult.MissingGetProp,
            ProbeApplication.EvaluateSafeWriteCapabilities(missingGetProp, write));
        Assert.AreEqual(
            ProbeApplication.SafeWriteCapabilityResult.Allowed,
            ProbeApplication.EvaluateSafeWriteCapabilities(allowed, write));
    }

    [TestMethod]
    [DataRow("set_power", "on", true)]
    [DataRow("set_power", "OFF", false)]
    [DataRow("set_bright", "1", true)]
    [DataRow("set_bright", "100", true)]
    [DataRow("set_bright", "0", false)]
    [DataRow("set_ct_abx", "1700", true)]
    [DataRow("set_ct_abx", "6500", true)]
    [DataRow("set_ct_abx", "<missing>", false)]
    [DataRow("set_rgb", "16777215", true)]
    [DataRow("bg_set_rgb", "16777216", false)]
    [DataRow("bg_set_power", "", false)]
    public void SafeWritePreReadRequiresAValidCurrentValue(
        string method,
        string currentValue,
        bool expected)
    {
        string requestedValue = method switch
        {
            "set_power" or "bg_set_power" => "on",
            "set_ct_abx" => "1700",
            _ => "1",
        };
        ProbeApplication.SafeWriteSpec write = ParseSafeWrite(
            method,
            requestedValue);

        Assert.AreEqual(
            expected,
            ProbeApplication.IsValidCurrentPropertyValue(write, currentValue));
    }

    [TestMethod]
    public void Lamp15SetPowerVerificationUsesIndependentChannelProperties()
    {
        ProbeApplication.SafeWriteSpec write = ParseSafeWrite("set_power", "off");

        ProbeApplication.SafeWriteVerificationPlan lamp15 =
            ProbeApplication.CreateSafeWriteVerificationPlan(" LAMP15 ", write);
        ProbeApplication.SafeWriteVerificationPlan unknown =
            ProbeApplication.CreateSafeWriteVerificationPlan("unknown", write);

        CollectionAssert.AreEqual(
            Lamp15PowerProperties,
            lamp15.Properties.ToArray());
        Assert.IsTrue(lamp15.IsLamp15MainPower);
        CollectionAssert.AreEqual(
            GenericPowerProperties,
            unknown.Properties.ToArray());
        Assert.IsFalse(unknown.IsLamp15MainPower);
    }

    [TestMethod]
    public void Lamp15SetPowerExpectedStatePreservesBackgroundAndAggregatesPower()
    {
        ProbeApplication.SafeWriteSpec write = ParseSafeWrite("set_power", "off");
        ProbeApplication.SafeWriteVerificationPlan plan =
            ProbeApplication.CreateSafeWriteVerificationPlan("lamp15", write);
        var before = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["power"] = "on",
            ["main_power"] = "on",
            ["bg_power"] = "on",
        };

        bool valid = ProbeApplication.TryCreateExpectedSafeWriteState(
            plan,
            write,
            before,
            out IReadOnlyDictionary<string, string> expected,
            out string error);

        Assert.IsTrue(valid, error);
        Assert.AreEqual("on", expected["power"]);
        Assert.AreEqual("off", expected["main_power"]);
        Assert.AreEqual("on", expected["bg_power"]);
    }

    [TestMethod]
    public void Lamp15SetPowerRejectsInconsistentPreReadAndChangedBackground()
    {
        ProbeApplication.SafeWriteSpec write = ParseSafeWrite("set_power", "off");
        ProbeApplication.SafeWriteVerificationPlan plan =
            ProbeApplication.CreateSafeWriteVerificationPlan("lamp15", write);
        var inconsistentBefore = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["power"] = "off",
            ["main_power"] = "on",
            ["bg_power"] = "on",
        };

        Assert.IsFalse(
            ProbeApplication.TryCreateExpectedSafeWriteState(
                plan,
                write,
                inconsistentBefore,
                out _,
                out string beforeError));
        StringAssert.Contains(beforeError, "写前电源状态不一致");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["power"] = "on",
            ["main_power"] = "off",
            ["bg_power"] = "on",
        };
        var changedBackground = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["power"] = "on",
            ["main_power"] = "off",
            ["bg_power"] = "off",
        };

        Assert.IsFalse(
            ProbeApplication.TryValidateSafeWriteState(
                expected,
                changedBackground,
                out string afterError));
        StringAssert.Contains(afterError, "bg_power 期望值为 on");
    }

    [TestMethod]
    public async Task UnconfirmedSafeWriteIsRejectedBeforeAnyNetworkOperation()
    {
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"LibraTray-Probe-Unconfirmed-{Guid.NewGuid():N}.jsonl");

        try
        {
            int exitCode = await ProbeApplication.RunAsync(
            [
                "safe-write",
                "--host",
                "127.0.0.1",
                "--method",
                "set_power",
                "--value",
                "on",
                "--log-path",
                logPath,
            ]);

            Assert.AreEqual(5, exitCode);
            Assert.IsTrue(File.Exists(logPath));
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void FindDiscoveryRecordRequiresSenderAndControlAddressToMatch()
    {
        var expectedAddress = IPAddress.Parse("192.0.2.10");
        DiscoveryRecord consistent = CreateRecord(
            senderAddress: expectedAddress,
            controlAddress: expectedAddress,
            controlPort: 55_443);
        DiscoveryRecord injected = CreateRecord(
            senderAddress: IPAddress.Parse("192.0.2.11"),
            controlAddress: expectedAddress,
            controlPort: 55_443);

        DiscoveryRecord? selected = ProbeApplication.FindDiscoveryRecord(
            [injected, consistent],
            expectedAddress,
            requiredPort: 55_443);

        Assert.AreSame(consistent, selected);
        Assert.IsNull(
            ProbeApplication.FindDiscoveryRecord(
                [injected],
                expectedAddress,
                requiredPort: 55_443));
    }

    [TestMethod]
    public void FindDiscoveryRecordRequiresExactControlPortWhenSpecified()
    {
        var address = IPAddress.Parse("192.0.2.20");
        DiscoveryRecord record = CreateRecord(
            senderAddress: address,
            controlAddress: address,
            controlPort: 55_443);

        Assert.AreSame(
            record,
            ProbeApplication.FindDiscoveryRecord(
                [record],
                address,
                requiredPort: 55_443));
        Assert.IsNull(
            ProbeApplication.FindDiscoveryRecord(
                [record],
                address,
                requiredPort: 55_444));
    }

    private static DiscoveryRecord CreateRecord(
        IPAddress senderAddress,
        IPAddress controlAddress,
        int controlPort,
        string support = "get_prop set_bright")
    {
        string raw =
            "HTTP/1.1 200 OK\r\n"
            + $"Location: yeelight://{controlAddress}:{controlPort}\r\n"
            + $"support: {support}\r\n"
            + "\r\n";
        YeelightDiscoveryResponse response = YeelightDiscovery.ParseResponse(
            Encoding.ASCII.GetBytes(raw));
        return new DiscoveryRecord(
            new IPEndPoint(senderAddress, YeelightDiscovery.MulticastPort),
            response);
    }

    private static ProbeApplication.SafeWriteSpec ParseSafeWrite(
        string method,
        string value)
    {
        ProbeOptions options = ProbeOptions.Parse(
        [
            "safe-write",
            "--host",
            "127.0.0.1",
            "--method",
            method,
            "--value",
            value,
        ]);
        return ProbeApplication.ParseSafeWrite(options);
    }
}
