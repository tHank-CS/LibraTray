using LibraTray.Core.Configuration;
using LibraTray.Core.Devices.LibraPro;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Configuration;

[TestClass]
public sealed class SegmentRgbRuntimeStateStoreTests
{
    private static readonly string[] ExpectedFirmwares = ["39", "40"];
    private string _directory = null!;
    private string _statePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"LibraTraySegmentStateTests-{Guid.NewGuid():N}");
        _statePath = Path.Combine(_directory, "segment-rgb-state.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveAndLoadRoundTripsRequestedValuesAndFirmwareAcknowledgements()
    {
        var store = new SegmentRgbRuntimeStateStore(_statePath);
        string key = SegmentRgbRuntimeStateStore.CreateDeviceKey("device-1")!;

        store.Save(new SegmentRgbRuntimeState
        {
            DeviceKey = key,
            LastColorMode = AmbientColorMode.Segmented,
            LastSegmentRequest = new SegmentRgbRequest(0x112233, 0x445566),
            AcknowledgedFirmwareVersions = ["39", "39", " 40 "],
            UpdatedUtc = DateTimeOffset.UtcNow,
        });

        SegmentRgbRuntimeState loaded = store.Load()!;
        Assert.AreEqual(AmbientColorMode.Segmented, loaded.LastColorMode);
        Assert.AreEqual(0x112233, loaded.LastSegmentRequest!.LeftRgb);
        CollectionAssert.AreEqual(
            ExpectedFirmwares,
            loaded.AcknowledgedFirmwareVersions.ToArray());
        Assert.IsFalse(File.Exists(_statePath + ".tmp"));
    }

    [TestMethod]
    public void CorruptOrIncompleteStateIsIgnored()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_statePath, "{not-json");
        var store = new SegmentRgbRuntimeStateStore(_statePath);
        Assert.IsNull(store.Load());

        File.WriteAllText(
            _statePath,
            """
            { "SchemaVersion": 1, "DeviceKey": "BAD", "LastColorMode": 1 }
            """);
        Assert.IsNull(store.Load());
    }
}
