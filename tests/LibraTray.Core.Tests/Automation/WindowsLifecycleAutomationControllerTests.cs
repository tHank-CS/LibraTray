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
    private List<LibraProTargetState> _targetWrites = null!;
    private List<(bool MainPower, bool BackgroundPower)> _powerWrites = null!;

    [TestInitialize]
    public void Initialize()
    {
        _now = InitialTime;
        _state = CreateState(mainPower: true, backgroundPower: true);
        _targetWrites = [];
        _powerWrites = [];
    }

    [TestMethod]
    public async Task DisabledAutomationDoesNotWrite()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings());

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);
        await controller.HandleAsync(WindowsLifecycleEventKind.SessionUnlocked);

        Assert.IsEmpty(_powerWrites);
        Assert.IsEmpty(_targetWrites);
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

        Assert.HasCount(1, _powerWrites);
        Assert.AreEqual((false, false), _powerWrites[0]);
        Assert.HasCount(1, _targetWrites);
        Assert.IsTrue(_targetWrites[0].MainPower);
        Assert.IsTrue(_targetWrites[0].BackgroundPower);
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
        Assert.HasCount(1, _powerWrites);
        Assert.IsEmpty(_targetWrites);

        await controller.HandleAsync(WindowsLifecycleEventKind.DisplayOn);

        Assert.HasCount(1, _powerWrites);
        Assert.HasCount(1, _targetWrites);
        Assert.IsTrue(_targetWrites[0].MainPower);
    }

    [TestMethod]
    public async Task ScreenSaverLockBeforeDisplayOnPreventsRestoreThenSecondOff()
    {
        using WindowsLifecycleAutomationController controller =
            CreateController(new WindowsAutomationSettings
            {
                LockAndUnlockEnabled = true,
                DisplayPowerEnabled = true,
            });

        await controller.HandleAsync(WindowsLifecycleEventKind.DisplayOff);
        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);
        await controller.HandleAsync(WindowsLifecycleEventKind.DisplayOn);

        Assert.HasCount(1, _powerWrites);
        Assert.AreEqual((false, false), _powerWrites[0]);
        Assert.IsEmpty(_targetWrites);

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);
        Assert.HasCount(1, _powerWrites);
        Assert.IsEmpty(_targetWrites);

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionUnlocked);
        Assert.HasCount(1, _powerWrites);
        Assert.HasCount(1, _targetWrites);
        Assert.IsTrue(_targetWrites[0].MainPower);
        Assert.IsTrue(_targetWrites[0].BackgroundPower);
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

        Assert.IsEmpty(_powerWrites);
        Assert.IsEmpty(_targetWrites);
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

        Assert.HasCount(1, _powerWrites);
        Assert.IsEmpty(_targetWrites);
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

        Assert.HasCount(1, _powerWrites);
        Assert.IsEmpty(_targetWrites);
    }

    [TestMethod]
    public async Task SegmentedLockUsesPowerOnlyWriteBeforeRestore()
    {
        var segment = new SegmentRgbRequest(0x13FF00, 0x0000FF);
        var target = new LibraProTargetState(
            true,
            _state.MainBrightness,
            _state.MainColorTemperature,
            true,
            _state.BackgroundBrightness,
            _state.BackgroundRgb,
            AmbientColorMode.Segmented,
            segment);
        using WindowsLifecycleAutomationController controller =
            CreateController(
                new WindowsAutomationSettings
                {
                    LockAndUnlockEnabled = true,
                },
                () => target);

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionLocked);

        Assert.HasCount(1, _powerWrites);
        Assert.AreEqual((false, false), _powerWrites[0]);
        Assert.IsEmpty(_targetWrites);
        Assert.AreEqual(60, _state.MainBrightness);
        Assert.AreEqual(40, _state.BackgroundBrightness);
        Assert.AreEqual(0x3366CC, _state.BackgroundRgb);

        await controller.HandleAsync(WindowsLifecycleEventKind.SessionUnlocked);

        Assert.HasCount(1, _targetWrites);
        Assert.AreEqual(AmbientColorMode.Segmented, _targetWrites[0].AmbientColorMode);
        Assert.AreEqual(segment, _targetWrites[0].SegmentRgb);
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
        WindowsAutomationSettings settings,
        Func<LibraProTargetState?>? readCurrentTargetState = null) =>
        new(
            settings,
            () => _state,
            (target, _) =>
            {
                _targetWrites.Add(target);
                _state = Apply(_state, target);
                return Task.FromResult(_state);
            },
            _ => Task.FromResult<LibraProState?>(_state),
            getUtcNow: () => _now,
            readCurrentTargetState: readCurrentTargetState,
            applyPowerState: (mainPower, backgroundPower, _) =>
            {
                _powerWrites.Add((mainPower, backgroundPower));
                _state = _state with
                {
                    AggregatePower = mainPower || backgroundPower,
                    MainPower = mainPower,
                    BackgroundPower = backgroundPower,
                };
                return Task.FromResult(_state);
            });

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
