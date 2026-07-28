using LibraTray.Core.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Identity;

[TestClass]
public sealed class ProductIdentityMapperTests
{
    private static readonly string[] ExpectedAliases =
    [
        "Yeelight LED Screen Light Bar Pro",
        "Yeelight Monitor Light Bar Pro",
        "Yeelight Screen Light Bar Pro",
    ];

    [TestMethod]
    [DataRow("lamp15")]
    [DataRow(" LAMP15 ")]
    [DataRow("\tlAmP15\r\n")]
    public void ResolveExactLamp15IgnoresCaseAndOuterWhitespace(string model)
    {
        DeviceIdentity identity = ProductIdentityMapper.Resolve(
            model,
            "Device-reported name");

        Assert.IsTrue(identity.IsKnownProduct);
        Assert.AreEqual("Yeelight Libra Pro", identity.FriendlyProductName);
        Assert.AreEqual("YLTD003", identity.HardwareModel);
        Assert.AreEqual(model, identity.InternalModel);
        Assert.AreEqual("Device-reported name", identity.ReportedName);
        Assert.AreEqual("Yeelight Libra Pro", identity.DisplayName);
    }

    [TestMethod]
    public void ResolveNormalizationForMatchingPreservesRawDiscoveryModel()
    {
        const string RawModel = " \tLAMP15\r\n";

        DeviceIdentity identity = ProductIdentityMapper.Resolve(RawModel);

        Assert.IsTrue(identity.IsKnownProduct);
        Assert.AreEqual(RawModel, identity.InternalModel);
        Assert.AreEqual("Yeelight Libra Pro", identity.DisplayName);
    }

    [TestMethod]
    [DataRow("lamp 15")]
    [DataRow("Libra")]
    [DataRow("Libra2")]
    [DataRow("Yeelight Libra 2")]
    [DataRow("Pro2")]
    [DataRow("YLTD001")]
    [DataRow("Yeelight Monitor Light Bar Pro")]
    public void ResolveNearOrSalesNamesDoesNotGuessLamp15(string model)
    {
        DeviceIdentity identity = ProductIdentityMapper.Resolve(
            model,
            "Reported fallback");

        Assert.IsFalse(identity.IsKnownProduct);
        Assert.IsNull(identity.HardwareModel);
        Assert.IsNull(identity.FriendlyProductName);
        Assert.AreEqual("Reported fallback", identity.DisplayName);
    }

    [TestMethod]
    public void ResolveUnknownWithoutReportedNameUsesSafeGenericDisplayName()
    {
        DeviceIdentity identity = ProductIdentityMapper.Resolve("unknown-model");

        Assert.IsNull(identity.FriendlyProductName);
        Assert.AreEqual("Unknown Yeelight device", identity.DisplayName);
        Assert.AreEqual("unknown-model", identity.InternalModel);
    }

    [TestMethod]
    public void ResolveUnknownPreservesRawReportedNameButSanitizesDisplayName()
    {
        const string RawName = "\0 \tDesk\r\nLight\u001b\u007f";

        DeviceIdentity identity = ProductIdentityMapper.Resolve(
            "unknown-model",
            RawName);

        Assert.AreEqual(RawName, identity.ReportedName);
        Assert.AreEqual("Desk Light", identity.DisplayName);
        Assert.IsFalse(identity.DisplayName.Any(char.IsControl));
    }

    [TestMethod]
    public void ResolveUnknownBoundsReportedDisplayNameWithoutSplittingUnicode()
    {
        string rawName = new string('a', 127) + "😀tail";

        DeviceIdentity identity = ProductIdentityMapper.Resolve(
            "unknown-model",
            rawName);

        Assert.AreEqual(new string('a', 127), identity.DisplayName);
        Assert.AreEqual(rawName, identity.ReportedName);
    }

    [TestMethod]
    public void WithUserAliasPreservesProductIdentityAndClearingRestoresDefault()
    {
        DeviceIdentity original = ProductIdentityMapper.Resolve(
            "lamp15",
            "Original reported name");

        DeviceIdentity renamed = original.WithUserAlias("  Desk light  ");

        Assert.AreEqual("Desk light", renamed.UserAlias);
        Assert.AreEqual("Desk light", renamed.DisplayName);
        Assert.AreEqual(original.FriendlyProductName, renamed.FriendlyProductName);
        Assert.AreEqual(original.HardwareModel, renamed.HardwareModel);
        Assert.AreEqual(original.InternalModel, renamed.InternalModel);
        Assert.AreEqual(original.ReportedName, renamed.ReportedName);

        DeviceIdentity cleared = renamed.WithUserAlias(" \t ");

        Assert.IsNull(cleared.UserAlias);
        Assert.AreEqual("Yeelight Libra Pro", cleared.DisplayName);
        Assert.AreEqual("Original reported name", cleared.ReportedName);
    }

    [TestMethod]
    public void CatalogAliasesAreInformationalAndDoNotMapByName()
    {
        CollectionAssert.AreEqual(
            ExpectedAliases,
            ProductIdentityCatalog.LibraProAliases.ToArray());

        foreach (string alias in ProductIdentityCatalog.LibraProAliases)
        {
            Assert.IsFalse(ProductIdentityMapper.IsSupportedInternalModel(alias));
        }
    }
}
