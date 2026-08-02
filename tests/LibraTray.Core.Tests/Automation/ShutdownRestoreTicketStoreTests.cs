using LibraTray.Core.Automation;
using LibraTray.Core.Devices.LibraPro;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Automation;

[TestClass]
public sealed class ShutdownRestoreTicketStoreTests
{
    private string _directory = null!;
    private string _ticketPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "LibraTray.Tests",
            Guid.NewGuid().ToString("N"));
        _ticketPath = Path.Combine(_directory, "shutdown-restore.json");
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
    public void SaveLoadAndClearRoundTripsTicketAtomically()
    {
        var store = new ShutdownRestoreTicketStore(_ticketPath);
        var ticket = new ShutdownRestoreTicket
        {
            DeviceKey = "ABC123",
            CreatedUtc = new DateTimeOffset(
                2026,
                8,
                2,
                1,
                2,
                3,
                TimeSpan.Zero),
            RestoreTarget = CreateTarget(),
        };

        store.Save(ticket);
        ShutdownRestoreTicket? loaded = store.Load();

        Assert.IsNotNull(loaded);
        Assert.AreEqual(ticket.DeviceKey, loaded.DeviceKey);
        Assert.AreEqual(ticket.CreatedUtc, loaded.CreatedUtc);
        Assert.AreEqual(ticket.RestoreTarget, loaded.RestoreTarget);
        Assert.IsEmpty(Directory.GetFiles(_directory, "*.tmp"));

        store.Clear();
        Assert.IsNull(store.Load());
    }

    [TestMethod]
    public void CorruptOrIncompleteTicketIsRejected()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_ticketPath, "{not-json");
        var store = new ShutdownRestoreTicketStore(_ticketPath);
        Assert.IsNull(store.Load());

        File.WriteAllText(
            _ticketPath,
            """{"SchemaVersion":1,"DeviceKey":"ABC"}""");
        Assert.IsNull(store.Load());
    }

    private static LibraProTargetState CreateTarget() =>
        new(true, 60, 4_500, true, 40, 0x3366CC);
}
