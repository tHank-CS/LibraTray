using System.Net;
using LibraTray.Core.Devices.LibraPro;
using LibraTray.Core.Diagnostics;
using LibraTray.Core.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Diagnostics;

[TestClass]
public sealed class LibraProDiagnosticReportTests
{
    [TestMethod]
    public void CreateRedactsDeviceAddressNamesAndIdentifier()
    {
        DeviceIdentity identity = ProductIdentityMapper
            .Resolve("lamp15", "Private room")
            .WithUserAlias("My desk");
        var connection = new LibraProConnectionInfo(
            "0x0000000000001234",
            "lamp15",
            "38",
            ["get_prop", "set_power"],
            new IPEndPoint(IPAddress.Parse("192.0.2.20"), 55_443),
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        LibraProState state = CreateState();

        string report = LibraProDiagnosticReport.Create(
            "0.5.0",
            LibraProSessionStatus.Connected,
            identity,
            connection,
            state,
            new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.Zero));

        Assert.IsFalse(report.Contains("192.0.2.20", StringComparison.Ordinal));
        Assert.IsFalse(
            report.Contains("0x0000000000001234", StringComparison.Ordinal));
        Assert.IsFalse(report.Contains("Private room", StringComparison.Ordinal));
        Assert.IsFalse(report.Contains("My desk", StringComparison.Ordinal));
        StringAssert.Contains(report, "DeviceId=[REDACTED-DEVICE-ID]");
        StringAssert.Contains(
            report,
            "ControlEndpoint=[REDACTED-LAN-ENDPOINT]");
        StringAssert.Contains(report, "InternalModel=lamp15");
        StringAssert.Contains(report, "FirmwareVersion=38");
        StringAssert.Contains(report, "MainBrightness=60");
    }

    [TestMethod]
    public void CreateWithoutConnectionProducesBoundedSafeFallback()
    {
        string report = LibraProDiagnosticReport.Create(
            "1.0\r\nInjected=true",
            LibraProSessionStatus.Disconnected,
            identity: null,
            connectionInfo: null,
            state: null,
            new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.Zero));

        Assert.IsFalse(
            report.Contains("\r\nInjected=true", StringComparison.Ordinal));
        StringAssert.Contains(report, "SessionStatus=Disconnected");
        StringAssert.Contains(report, "ConfirmedState=not-available");
    }

    private static LibraProState CreateState() =>
        new(
            true,
            true,
            false,
            60,
            4_500,
            40,
            4_000,
            0x3366CC,
            220,
            60,
            1);
}
