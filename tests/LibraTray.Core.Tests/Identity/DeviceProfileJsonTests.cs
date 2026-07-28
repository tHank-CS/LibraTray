using LibraTray.Core.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Identity;

[TestClass]
public sealed class DeviceProfileJsonTests
{
    [TestMethod]
    public void SerializeAndDeserializePreservesSeparatedIdentityFieldsAndAlias()
    {
        DeviceIdentity identity = ProductIdentityMapper
            .Resolve("lamp15", "Raw device name")
            .WithUserAlias("Work light");

        string json = DeviceProfileJson.Serialize(identity);
        DeviceIdentity restored = DeviceProfileJson.DeserializeOrDefault(
            json,
            ProductIdentityMapper.Resolve("unknown"));

        Assert.AreEqual("Yeelight Libra Pro", restored.FriendlyProductName);
        Assert.AreEqual("YLTD003", restored.HardwareModel);
        Assert.AreEqual("lamp15", restored.InternalModel);
        Assert.AreEqual("Raw device name", restored.ReportedName);
        Assert.AreEqual("Work light", restored.UserAlias);
    }

    [TestMethod]
    public void DeserializeCorruptJsonReturnsCompleteFallbackIdentity()
    {
        DeviceIdentity fallback = ProductIdentityMapper.Resolve(
            "lamp15",
            "Still preserved");

        DeviceIdentity restored = DeviceProfileJson.DeserializeOrDefault(
            """{"FriendlyProductName":"broken","InternalModel":""",
            fallback);

        Assert.AreSame(fallback, restored);
        Assert.AreEqual("Yeelight Libra Pro", restored.DisplayName);
        Assert.AreEqual("YLTD003", restored.HardwareModel);
        Assert.AreEqual("lamp15", restored.InternalModel);
        Assert.AreEqual("Still preserved", restored.ReportedName);
    }

    [TestMethod]
    public void DeserializeDerivedNamesAreTamperedReDerivesFromExactInternalModel()
    {
        const string Json = """
            {
              "FriendlyProductName": "Impostor",
              "HardwareModel": "YLTD001",
              "InternalModel": " LAMP15 ",
              "ReportedName": "Protocol name",
              "UserAlias": null,
              "UnknownFutureField": true
            }
            """;

        DeviceIdentity restored = DeviceProfileJson.DeserializeOrDefault(
            Json,
            ProductIdentityMapper.Resolve("fallback"));

        Assert.IsTrue(restored.IsKnownProduct);
        Assert.AreEqual("Yeelight Libra Pro", restored.FriendlyProductName);
        Assert.AreEqual("YLTD003", restored.HardwareModel);
        Assert.AreEqual(" LAMP15 ", restored.InternalModel);
        Assert.AreEqual("Protocol name", restored.ReportedName);
    }

    [TestMethod]
    public void DeserializeStructurallyIncompleteProfileReturnsFallback()
    {
        DeviceIdentity fallback = ProductIdentityMapper.Resolve("lamp15");

        DeviceIdentity restored = DeviceProfileJson.DeserializeOrDefault(
            """{"UserAlias":"Should not replace defaults"}""",
            fallback);

        Assert.AreSame(fallback, restored);
        Assert.IsNull(restored.UserAlias);
        Assert.AreEqual("Yeelight Libra Pro", restored.DisplayName);
    }

    [TestMethod]
    public void UnknownProductProfileRoundTripKeepsFriendlyMappingUnset()
    {
        DeviceIdentity unknown = ProductIdentityMapper
            .Resolve("future-model", " Future device ")
            .WithUserAlias("Lab device");

        DeviceIdentity restored = DeviceProfileJson.DeserializeOrDefault(
            DeviceProfileJson.Serialize(unknown),
            ProductIdentityMapper.Resolve("fallback"));

        Assert.IsFalse(restored.IsKnownProduct);
        Assert.IsNull(restored.FriendlyProductName);
        Assert.IsNull(restored.HardwareModel);
        Assert.AreEqual("future-model", restored.InternalModel);
        Assert.AreEqual(" Future device ", restored.ReportedName);
        Assert.AreEqual("Lab device", restored.DisplayName);
    }
}
