using LibraTray.Core.Automation;
using LibraTray.Core.Configuration;
using LibraTray.Core.Devices.LibraPro;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Automation;

[TestClass]
public sealed class ShutdownRestoreCoordinatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 2, 2, 0, 0, TimeSpan.Zero);

    private MemoryStore _store = null!;
    private LibraProState _state = null!;
    private string? _deviceId;
    private List<LibraProTargetState> _writes = null!;

    [TestInitialize]
    public void Initialize()
    {
        _store = new MemoryStore();
        _state = CreateState(mainPower: true, backgroundPower: true);
        _deviceId = "0x0000000000001234";
        _writes = [];
    }

    [TestMethod]
    public async Task ConfirmedShutdownOffCreatesOneTimeTicket()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();

        await coordinator.PrepareForShutdownAsync();

        Assert.HasCount(1, _writes);
        Assert.IsFalse(_state.MainPower);
        Assert.IsFalse(_state.BackgroundPower);
        Assert.IsNotNull(_store.Ticket);
        Assert.AreNotEqual(_deviceId, _store.Ticket.DeviceKey);
        Assert.IsTrue(_store.Ticket.RestoreTarget!.MainPower);
    }

    [TestMethod]
    public void QueryStagePersistsTicketWithoutSendingPowerWrite()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();

        bool saved = coordinator.PrepareTicketForPotentialShutdown();

        Assert.IsTrue(saved);
        Assert.IsEmpty(_writes);
        Assert.IsNotNull(_store.Ticket);
        Assert.IsTrue(_store.Ticket.RestoreTarget!.MainPower);
        Assert.IsTrue(_state.MainPower);
        Assert.IsTrue(_state.BackgroundPower);
    }

    [TestMethod]
    public void CanceledQueryStageClearsPreparedTicket()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();
        _ = coordinator.PrepareTicketForPotentialShutdown();

        coordinator.CancelPotentialShutdown();

        Assert.IsNull(_store.Ticket);
        Assert.IsEmpty(_writes);
    }

    [TestMethod]
    public void QueryStageDoesNotPersistAlreadyOffState()
    {
        _state = CreateState(mainPower: false, backgroundPower: false);
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();

        bool saved = coordinator.PrepareTicketForPotentialShutdown();

        Assert.IsFalse(saved);
        Assert.IsNull(_store.Ticket);
        Assert.IsEmpty(_writes);
    }

    [TestMethod]
    public async Task AlreadyOffWithoutAutomationSnapshotCreatesNoTicket()
    {
        _state = CreateState(mainPower: false, backgroundPower: false);
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();

        await coordinator.PrepareForShutdownAsync();

        Assert.IsEmpty(_writes);
        Assert.IsNull(_store.Ticket);
    }

    [TestMethod]
    public async Task StartupRestoresOnlyExpectedOffStateAndConsumesTicket()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();
        await coordinator.PrepareForShutdownAsync();
        _writes.Clear();

        await coordinator.TryRestoreAfterStartupAsync();

        Assert.HasCount(1, _writes);
        Assert.IsTrue(_state.MainPower);
        Assert.IsTrue(_state.BackgroundPower);
        Assert.IsNull(_store.Ticket);
    }

    [TestMethod]
    public async Task StartupClearsTicketWhenDeviceNeverTurnedOff()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();
        await coordinator.PrepareForShutdownAsync();
        _state = CreateState(mainPower: true, backgroundPower: true);
        _writes.Clear();

        await coordinator.TryRestoreAfterStartupAsync();

        Assert.IsEmpty(_writes);
        Assert.IsNull(_store.Ticket);
        Assert.IsTrue(_state.MainPower);
        Assert.IsTrue(_state.BackgroundPower);
    }

    [TestMethod]
    public async Task NewerDeviceStateIsNeverOverwritten()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();
        await coordinator.PrepareForShutdownAsync();
        _writes.Clear();
        _state = CreateState(mainPower: true, backgroundPower: false);

        await coordinator.TryRestoreAfterStartupAsync();

        Assert.IsEmpty(_writes);
        Assert.IsNull(_store.Ticket);
        Assert.IsTrue(_state.MainPower);
    }

    [TestMethod]
    public async Task DifferentDeviceDoesNotReceiveSavedState()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();
        await coordinator.PrepareForShutdownAsync();
        _writes.Clear();
        _deviceId = "another-device";

        await coordinator.TryRestoreAfterStartupAsync();

        Assert.IsEmpty(_writes);
        Assert.IsNull(_store.Ticket);
    }

    [TestMethod]
    public async Task ExpiredTicketIsDiscarded()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();
        await coordinator.PrepareForShutdownAsync();
        _store.Ticket = _store.Ticket! with
        {
            CreatedUtc = Now.AddDays(-8),
        };
        _writes.Clear();

        await coordinator.TryRestoreAfterStartupAsync();

        Assert.IsEmpty(_writes);
        Assert.IsNull(_store.Ticket);
    }

    [TestMethod]
    public async Task DisplayOffSnapshotCanBePersistedWithoutAnotherWrite()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();
        var target = new LibraProTargetState(
            _state.MainPower,
            _state.MainBrightness,
            _state.MainColorTemperature,
            _state.BackgroundPower,
            _state.BackgroundBrightness,
            _state.BackgroundRgb);
        _state = Apply(
            _state,
            new LibraProTargetState(
                false,
                target.MainBrightness,
                target.MainColorTemperature,
                false,
                target.BackgroundBrightness,
                target.BackgroundRgb));
        var snapshot = new AutomationRestoreSnapshot(
            target,
            PowerOffApplied: true);

        await coordinator.PrepareForShutdownAsync(snapshot);

        Assert.IsEmpty(_writes);
        Assert.IsNotNull(_store.Ticket);
        Assert.IsTrue(_store.Ticket.RestoreTarget!.MainPower);
    }

    [TestMethod]
    public async Task ManualActionDuringStartupCheckCancelsRestore()
    {
        ShutdownRestoreCoordinator? coordinator = null;
        coordinator = CreateCoordinator(
            refresh: _ =>
            {
                coordinator!.RecordManualControl();
                return Task.FromResult<LibraProState?>(_state);
            });
        using (coordinator)
        {
            await coordinator.PrepareForShutdownAsync();
            _writes.Clear();
            await coordinator.TryRestoreAfterStartupAsync();
        }

        Assert.IsEmpty(_writes);
        Assert.IsNull(_store.Ticket);
    }

    [TestMethod]
    public async Task ShutdownTicketIsSavedBeforePowerOnlyWriteStarts()
    {
        bool ticketWasPresentDuringWrite = false;
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator(
            applyPower: (mainPower, backgroundPower, _) =>
            {
                ticketWasPresentDuringWrite = _store.Ticket is not null;
                var target = new LibraProTargetState(
                    false,
                    _state.MainBrightness,
                    _state.MainColorTemperature,
                    false,
                    _state.BackgroundBrightness,
                    _state.BackgroundRgb);
                _writes.Add(target);
                _state = _state with
                {
                    AggregatePower = mainPower || backgroundPower,
                    MainPower = mainPower,
                    BackgroundPower = backgroundPower,
                };
                return Task.FromResult(_state);
            });

        await coordinator.PrepareForShutdownAsync();

        Assert.IsTrue(ticketWasPresentDuringWrite);
        Assert.IsNotNull(_store.Ticket);
        Assert.HasCount(1, _writes);
    }

    [TestMethod]
    public async Task ShutdownTimeoutLeavesSavedTicketForSafeStartupEvaluation()
    {
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(20));
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator(
            applyPower: async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return _state;
            });

        await coordinator.PrepareForShutdownAsync(
            cancellationToken: cancellation.Token);

        Assert.IsNotNull(_store.Ticket);
        Assert.IsTrue(_state.MainPower);
        Assert.IsTrue(_state.BackgroundPower);
    }

    [TestMethod]
    public async Task StartupPowerPolicyTurnsBothChannelsOnFromOffState()
    {
        _state = CreateState(mainPower: false, backgroundPower: false);
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator(
            settings: CreateStartupPowerSettings());
        ShutdownRestoreOutcome? outcome = null;
        coordinator.StatusChanged += (_, eventArgs) =>
            outcome = eventArgs.Outcome;

        await coordinator.ApplyStartupPowerPolicyAsync(
            coordinator.ManualControlVersion);

        Assert.HasCount(1, _writes);
        Assert.IsTrue(_state.MainPower);
        Assert.IsTrue(_state.BackgroundPower);
        Assert.AreEqual(ShutdownRestoreOutcome.LightsTurnedOn, outcome);
    }

    [TestMethod]
    public async Task DisabledStartupPowerPolicyNeverWrites()
    {
        _state = CreateState(mainPower: false, backgroundPower: false);
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator();

        await coordinator.ApplyStartupPowerPolicyAsync(
            coordinator.ManualControlVersion);

        Assert.IsEmpty(_writes);
        Assert.IsFalse(_state.MainPower);
        Assert.IsFalse(_state.BackgroundPower);
    }

    [TestMethod]
    public async Task ExplicitStartupPowerPolicyRunsAfterTicketRestore()
    {
        _state = CreateState(mainPower: true, backgroundPower: false);
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator(
            settings: CreateStartupPowerSettings());
        await coordinator.PrepareForShutdownAsync();
        _writes.Clear();

        await coordinator.TryRestoreAfterStartupAsync();
        await coordinator.ApplyStartupPowerPolicyAsync(
            coordinator.ManualControlVersion);

        Assert.HasCount(2, _writes);
        Assert.IsTrue(_state.MainPower);
        Assert.IsTrue(_state.BackgroundPower);
    }

    [TestMethod]
    public async Task StartupPowerPolicyDoesNotRewriteAlreadyOnState()
    {
        using ShutdownRestoreCoordinator coordinator = CreateCoordinator(
            settings: CreateStartupPowerSettings());

        await coordinator.ApplyStartupPowerPolicyAsync(
            coordinator.ManualControlVersion);

        Assert.IsEmpty(_writes);
        Assert.IsTrue(_state.MainPower);
        Assert.IsTrue(_state.BackgroundPower);
    }

    [TestMethod]
    public async Task ManualControlDuringStartupPowerRefreshCancelsWrite()
    {
        _state = CreateState(mainPower: false, backgroundPower: false);
        ShutdownRestoreCoordinator? coordinator = null;
        coordinator = CreateCoordinator(
            refresh: _ =>
            {
                coordinator!.RecordManualControl();
                return Task.FromResult<LibraProState?>(_state);
            },
            settings: CreateStartupPowerSettings());
        using (coordinator)
        {
            await coordinator.ApplyStartupPowerPolicyAsync(
                coordinator.ManualControlVersion);
        }

        Assert.IsEmpty(_writes);
        Assert.IsFalse(_state.MainPower);
        Assert.IsFalse(_state.BackgroundPower);
    }

    private ShutdownRestoreCoordinator CreateCoordinator(
        Func<CancellationToken, Task<LibraProState?>>? refresh = null,
        Func<bool, bool, CancellationToken, Task<LibraProState>>?
            applyPower = null,
        WindowsAutomationSettings? settings = null) =>
        new(
            settings ?? new WindowsAutomationSettings
            {
                ShutdownAndStartupEnabled = true,
            },
            _store,
            () => _deviceId,
            () => _state,
            (target, _) =>
            {
                _writes.Add(target);
                _state = Apply(_state, target);
                return Task.FromResult(_state);
            },
            refresh ?? (_ => Task.FromResult<LibraProState?>(_state)),
            () => Now,
            applyPowerState: applyPower);

    private static WindowsAutomationSettings CreateStartupPowerSettings() =>
        new()
        {
            StartWithWindows = true,
            TurnOnLightsAfterWindowsStartup = true,
            ShutdownAndStartupEnabled = true,
        };

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

    private sealed class MemoryStore : IShutdownRestoreTicketStore
    {
        public ShutdownRestoreTicket? Ticket { get; set; }

        public ShutdownRestoreTicket? Load() => Ticket;

        public void Save(ShutdownRestoreTicket ticket) => Ticket = ticket;

        public void Clear() => Ticket = null;
    }
}
