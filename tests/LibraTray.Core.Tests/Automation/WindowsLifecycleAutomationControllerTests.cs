using LibraTray.Core.Automation;
using LibraTray.Core.Configuration;
using LibraTray.Core.Devices.LibraPro;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Automation;

[TestClass]
public sealed class WindowsLifecycleAutomationControllerTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now;
    private LibraProState _state = null!;
    private List<LibraProTargetState> _writes = null!;

    [TestInitialize]
    public void Initialize()
    {
        _now = InitialTime;
        _state = CreateState(mainPower: true, backgroundPower: true);
        _writes = [];
    }

    [TestMethod]
    public async Task DisabledAutomationDoesNotWrite()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings());

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);
        await controller.HandleAsync(WindowsLifecycleEventKind.SessionUnlocked);

        Assert.IsEmpty(_writes);
    }

    [TestMethod]
    public async Task LockThenUnlockTurnsOffAndRestoresSnapshot()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings
            {
                LockAndUnlockEnabled = true,
            });

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);
        await controller.HandleAsync(WindowsLifecycleEventKind.SessionUnlocked);

        Assert.HasCount(2, _writes);
        Assert.IsFalse(_writes[0].MainPower);
        Assert.IsFalse(_writes[0].BackgroundPower);
        Assert.IsTrue(_writes[1].MainPower);
        Assert.IsTrue(_writes[1].BackgroundPower);
    }

    [TestMethod]
    public async Task OverlappingLockAndDisplayRestoreOnlyAfterBothEnd()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings
            {
                LockAndUnlockEnabled = true,
                DisplayPowerEnabled = true,
            });

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);
        await controller.HandleAsync(WindowsLifecycleEventKind.DisplayOff);
        await controller.HandleAsync(WindowsLifecycleEventKind.SessionUnlocked);
        Assert.HasCount(1, _writes);

        await controller.HandleAsync(WindowsLifecycleEventKind.DisplayOn);

        Assert.HasCount(2, _writes);
        Assert.IsTrue(_writes[1].MainPower);
    }

    [TestMethod]
    public async Task RecentManualControlSuppressesEntryAction()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings
            {
                LockAndUnlockEnabled = true,
                ManualSuppressionSeconds = 5,
            });
        controller.RecordManualControl();
        _now = _now.AddSeconds(2);

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);
        await controller.HandleAsync(WindowsLifecycleEventKind.SessionUnlocked);

        Assert.IsEmpty(_writes);
    }

    [TestMethod]
    public async Task ManualControlWhileBlockedCancelsRestore()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings
            {
                LockAndUnlockEnabled = true,
            });

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);
        controller.RecordManualControl();
        await controller.HandleAsync(WindowsLifecycleEventKind.SessionUnlocked);

        Assert.HasCount(1, _writes);
    }

    [TestMethod]
    public async Task NewerDeviceStateCancelsRestore()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings
            {
                DisplayPowerEnabled = true,
            });

        await controller.HandleAsync(WindowsLifecycleEventKind.DisplayOff);
        _state = CreateState(mainPower: true, backgroundPower: false);
        await controller.HandleAsync(WindowsLifecycleEventKind.DisplayOn);

        Assert.HasCount(1, _writes);
    }

    [TestMethod]
    public async Task ExposesConfirmedPendingSnapshotForSessionEnding()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings
            {
                DisplayPowerEnabled = true,
            });

        await controller.HandleAsync(WindowsLifecycleEventKind.DisplayOff);

        Assert.IsNotNull(controller.PendingRestoreSnapshot);
        Assert.IsTrue(controller.PendingRestoreSnapshot.PowerOffApplied);
        Assert.IsTrue(controller.PendingRestoreSnapshot.Target.MainPower);
        Assert.IsTrue(controller.PendingRestoreSnapshot.Target.BackgroundPower);
    }

    private WindowsLifecycleAutomationController CreateController(
        WindowsAutomationSettings settings) =>
        new(
            settings,
            () => _state,
            (target, _) =>
            {
                _writes.Add(target);
                _state = Apply(_state, target);
                return Task.FromResult(_state);
            },
            _ => Task.FromResult<LibraProState?>(_state),
            () => _now);

    private static LibraProState Apply(
        LibraProState state,
        LibraProTargetState target) =>
        state with
        {
            AggregatePower = target.MainPower || target.BackgroundPower,
            MainPower = target.MainPower,
            MainBrightness = target.MainBrightness,
            MainColorTemperature = target.MainColorTemperature,
            BackgroundPower = target.BackgroundPower,
            BackgroundBrightness = target.BackgroundBrightness,
            BackgroundRgb = target.BackgroundRgb,
        };

    private static LibraProState CreateState(
        bool mainPower,
        bool backgroundPower) =>
        new(
            mainPower || backgroundPower,
            mainPower,
            backgroundPower,
            60,
            4_500,
            40,
            4_000,
            0x3366CC,
            220,
            60,
            1);
}
