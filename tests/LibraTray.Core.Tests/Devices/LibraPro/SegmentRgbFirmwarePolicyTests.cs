using LibraTray.Core.Devices.LibraPro;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Devices.LibraPro;

[TestClass]
public sealed class SegmentRgbFirmwarePolicyTests
{
    private static readonly string[] SegmentCapability = ["set_segment_rgb"];

    [TestMethod]
    public void Firmware38IsAllowedOnlyForExactSupportedIdentityAndCapability()
    {
        Assert.AreEqual(
            SegmentRgbFirmwareAccess.Allowed,
            Evaluate("lamp15", "38"));
        Assert.AreEqual(
            SegmentRgbFirmwareAccess.Unsupported,
            Evaluate("lamp16", "38"));
        Assert.AreEqual(
            SegmentRgbFirmwareAccess.Unsupported,
            SegmentRgbFirmwarePolicy.Evaluate(
                "lamp15",
                [],
                "38",
                [],
                false));
    }

    [TestMethod]
    public void OtherFirmwareRequiresPersistentAcknowledgement()
    {
        Assert.AreEqual(
            SegmentRgbFirmwareAccess.RequiresFirmwareConfirmation,
            Evaluate("lamp15", "39"));
        Assert.AreEqual(
            SegmentRgbFirmwareAccess.Allowed,
            SegmentRgbFirmwarePolicy.Evaluate(
                "lamp15",
                SegmentCapability,
                "39",
                ["39"],
                false));
    }

    [TestMethod]
    public void UnknownFirmwareCanOnlyBeAcknowledgedForCurrentSession()
    {
        Assert.AreEqual(
            SegmentRgbFirmwareAccess.RequiresUnknownFirmwareConfirmation,
            Evaluate("lamp15", null));
        Assert.AreEqual(
            SegmentRgbFirmwareAccess.Allowed,
            SegmentRgbFirmwarePolicy.Evaluate(
                "lamp15",
                SegmentCapability,
                null,
                [],
                true));
    }

    private static SegmentRgbFirmwareAccess Evaluate(
        string model,
        string? firmware) =>
        SegmentRgbFirmwarePolicy.Evaluate(
            model,
            SegmentCapability,
            firmware,
            [],
            false);
}
