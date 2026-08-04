using LibraTray.Core.Devices.LibraPro;
using LibraTray.Core.Networking;
using LibraTray.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Devices.LibraPro;

[TestClass]
public sealed class LibraProAdapterTests
{
    private static readonly string[] Capabilities =
    [
        "get_prop",
        "set_power",
        "set_bright",
        "set_ct_abx",
        "bg_set_power",
        "bg_set_bright",
        "bg_set_ct_abx",
        "bg_set_rgb",
        "bg_set_scene",
    ];

    private static readonly string[] ExplicitRecoveryMethods =
        ["get_prop", "bg_set_scene", "get_prop", "bg_set_power", "get_prop"];

    [TestMethod]
    public async Task BackgroundPowerUsesOrdinaryVerifiedPathWhenFirmwareResponds()
    {
        var transport = new FakeTransport();
        LibraProAdapter adapter = CreateAdapter(transport);
        adapter.BeginConnectionEpoch(1);

        LibraProCommandResult result = await adapter.SetBackgroundPowerAsync(
            enabled: true,
            restoreSnapshot: null,
            TimeSpan.FromSeconds(1));

        Assert.IsTrue(result.State.BackgroundPower);
        Assert.IsFalse(result.State.MainPower);
        Assert.IsFalse(result.RecoveryAttempted);
        Assert.AreEqual(0, transport.SceneRefreshCount);
    }

    [TestMethod]
    public async Task FailedBackgroundOnTriggersOneSceneRefreshAndRestoresAppearance()
    {
        var transport = new FakeTransport
        {
            BackgroundRendererSilent = true,
        };
        LibraProAdapter adapter = CreateAdapter(transport);
        adapter.BeginConnectionEpoch(7);
        var snapshot = new LibraProBackgroundSnapshot(73, 65_280);

        LibraProCommandResult result = await adapter.SetBackgroundPowerAsync(
            enabled: true,
            snapshot,
            TimeSpan.FromSeconds(1));

        Assert.IsTrue(result.RecoveryAttempted);
        Assert.AreEqual(7L, result.ConnectionEpoch);
        Assert.IsTrue(result.State.BackgroundPower);
        Assert.IsFalse(result.State.MainPower);
        Assert.AreEqual(73, result.State.BackgroundBrightness);
        Assert.AreEqual(65_280, result.State.BackgroundRgb);
        Assert.AreEqual(1, transport.SceneRefreshCount);
    }

    [TestMethod]
    public async Task DisabledRecoveryReportsTheVerifiedPowerMismatch()
    {
        var transport = new FakeTransport
        {
            BackgroundRendererSilent = true,
        };
        LibraProAdapter adapter = CreateAdapter(
            transport,
            new LibraProAdapterOptions
            {
                ColdStartRecoveryMode = LibraProColdStartRecoveryMode.Disabled,
            });

        await Assert.ThrowsExactlyAsync<LibraProStateVerificationException>(
            () => adapter.SetBackgroundPowerAsync(
                enabled: true,
                restoreSnapshot: null,
                TimeSpan.FromSeconds(1)));
        Assert.AreEqual(0, transport.SceneRefreshCount);
    }

    [TestMethod]
    public async Task RecoveryIsBoundedToOneAttemptPerConnectionEpoch()
    {
        var transport = new FakeTransport
        {
            BackgroundRendererSilent = true,
            SceneRecoveryWorks = false,
        };
        LibraProAdapter adapter = CreateAdapter(transport);
        adapter.BeginConnectionEpoch(10);

        await Assert.ThrowsExactlyAsync<LibraProStateVerificationException>(
            () => adapter.SetBackgroundPowerAsync(
                enabled: true,
                restoreSnapshot: null,
                TimeSpan.FromSeconds(1)));
        await Assert.ThrowsExactlyAsync<LibraProStateVerificationException>(
            () => adapter.SetBackgroundPowerAsync(
                enabled: true,
                restoreSnapshot: null,
                TimeSpan.FromSeconds(1)));
        Assert.AreEqual(1, transport.SceneRefreshCount);

        adapter.BeginConnectionEpoch(11);
        await Assert.ThrowsExactlyAsync<LibraProStateVerificationException>(
            () => adapter.SetBackgroundPowerAsync(
                enabled: true,
                restoreSnapshot: null,
                TimeSpan.FromSeconds(1)));
        Assert.AreEqual(2, transport.SceneRefreshCount);
    }

    [TestMethod]
    public async Task ExplicitRecoveryCanRestoreAnOffPreDisconnectState()
    {
        var transport = new FakeTransport
        {
            BackgroundRendererSilent = true,
        };
        LibraProAdapter adapter = CreateAdapter(transport);
        adapter.BeginConnectionEpoch(3);

        LibraProCommandResult result = await adapter.RecoverBackgroundAsync(
            new LibraProBackgroundSnapshot(50, 13_395_711),
            desiredPower: false,
            TimeSpan.FromSeconds(1));

        Assert.IsTrue(result.RecoveryAttempted);
        Assert.IsFalse(result.State.MainPower);
        Assert.IsFalse(result.State.BackgroundPower);
        Assert.AreEqual(50, result.State.BackgroundBrightness);
        Assert.AreEqual(13_395_711, result.State.BackgroundRgb);
        CollectionAssert.AreEqual(
            ExplicitRecoveryMethods,
            transport.Methods.ToArray());
    }

    [TestMethod]
    public async Task RecoveryRejectsAnUnexpectedMainChannelChange()
    {
        var transport = new FakeTransport
        {
            BackgroundRendererSilent = true,
            SceneChangesMainPower = true,
        };
        LibraProAdapter adapter = CreateAdapter(transport);

        LibraProStateVerificationException exception =
            await Assert.ThrowsExactlyAsync<LibraProStateVerificationException>(
                () => adapter.SetBackgroundPowerAsync(
                    enabled: true,
                    restoreSnapshot: null,
                    TimeSpan.FromSeconds(1)));

        StringAssert.Contains(exception.Message, "main_power");
    }

    [TestMethod]
    public async Task QueryRejectsAggregatePowerThatConflictsWithBothChannels()
    {
        var transport = new FakeTransport
        {
            AggregateOverride = "on",
        };
        LibraProAdapter adapter = CreateAdapter(transport);

        await Assert.ThrowsExactlyAsync<YeelightProtocolException>(
            () => adapter.QueryStateAsync(TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task VerifiedMainAndBackgroundAppearanceCommandsReturnCompleteState()
    {
        var transport = new FakeTransport();
        LibraProAdapter adapter = CreateAdapter(transport);
        TimeSpan timeout = TimeSpan.FromSeconds(1);

        LibraProState state = (await adapter.SetMainPowerAsync(true, timeout)).State;
        Assert.IsTrue(state.MainPower);

        state = (await adapter.SetMainBrightnessAsync(42, timeout)).State;
        Assert.AreEqual(42, state.MainBrightness);

        state = (await adapter.SetMainColorTemperatureAsync(2_700, timeout)).State;
        Assert.AreEqual(2_700, state.MainColorTemperature);

        state = (await adapter.SetBackgroundBrightnessAsync(61, timeout)).State;
        Assert.AreEqual(61, state.BackgroundBrightness);

        state = (await adapter.SetBackgroundColorTemperatureAsync(3_000, timeout)).State;
        Assert.AreEqual(3_000, state.BackgroundColorTemperature);

        state = (await adapter.SetBackgroundRgbAsync(65_280, timeout)).State;
        Assert.AreEqual(65_280, state.BackgroundRgb);
    }

    [TestMethod]
    public async Task ApplyTargetStateWritesAppearanceBeforePowerAndVerifiesAllFields()
    {
        var transport = new FakeTransport();
        LibraProAdapter adapter = CreateAdapter(transport);
        var target = new LibraProTargetState(
            mainPower: false,
            mainBrightness: 64,
            mainColorTemperature: 4_800,
            backgroundPower: true,
            backgroundBrightness: 38,
            backgroundRgb: 0x3366CC);

        LibraProCommandResult result = await adapter.ApplyTargetStateAsync(
            target,
            TimeSpan.FromSeconds(1));

        Assert.IsFalse(result.State.MainPower);
        Assert.AreEqual(64, result.State.MainBrightness);
        Assert.AreEqual(4_800, result.State.MainColorTemperature);
        Assert.IsTrue(result.State.BackgroundPower);
        Assert.AreEqual(38, result.State.BackgroundBrightness);
        Assert.AreEqual(0x3366CC, result.State.BackgroundRgb);
        string[] expectedMethods =
        [
            "set_bright",
            "set_ct_abx",
            "bg_set_bright",
            "bg_set_rgb",
            "set_power",
            "bg_set_power",
            "get_prop",
        ];
        CollectionAssert.AreEqual(expectedMethods, transport.Methods);
    }

    [TestMethod]
    public async Task AppearanceCommandsRejectOutOfRangeValuesBeforeSending()
    {
        var transport = new FakeTransport();
        LibraProAdapter adapter = CreateAdapter(transport);
        TimeSpan timeout = TimeSpan.FromSeconds(1);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => adapter.SetMainBrightnessAsync(0, timeout));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => adapter.SetBackgroundBrightnessAsync(101, timeout));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => adapter.SetMainColorTemperatureAsync(2_699, timeout));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => adapter.SetBackgroundColorTemperatureAsync(6_501, timeout));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => adapter.SetBackgroundRgbAsync(16_777_216, timeout));

        Assert.HasCount(0, transport.Methods);
    }

    [TestMethod]
    public void AdapterRejectsUnknownProductIdentity()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new LibraProAdapter(
                new FakeTransport(),
                "unknown",
                Capabilities));
    }

    private static LibraProAdapter CreateAdapter(
        FakeTransport transport,
        LibraProAdapterOptions? options = null) =>
        new(
            transport,
            "lamp15",
            Capabilities,
            options);

    private sealed class FakeTransport : IYeelightCommandTransport
    {
        private readonly Dictionary<string, string> _state =
            new(StringComparer.Ordinal)
            {
                ["power"] = "off",
                ["main_power"] = "off",
                ["bg_power"] = "off",
                ["bright"] = "100",
                ["ct"] = "4000",
                ["bg_bright"] = "50",
                ["bg_ct"] = "4000",
                ["bg_rgb"] = "13395711",
                ["bg_hue"] = "359",
                ["bg_sat"] = "100",
                ["bg_lmode"] = "1",
            };

        public bool BackgroundRendererSilent { get; set; }

        public bool SceneRecoveryWorks { get; set; } = true;

        public bool SceneChangesMainPower { get; set; }

        public string? AggregateOverride { get; set; }

        public int SceneRefreshCount { get; private set; }

        public List<string> Methods { get; } = [];

        public Task<IReadOnlyList<string>> SendAsync(
            string method,
            IReadOnlyList<object?> parameters,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            _ = timeout;
            cancellationToken.ThrowIfCancellationRequested();
            Methods.Add(method);

            IReadOnlyList<string> result = method switch
            {
                "get_prop" => ReadProperties(parameters),
                "set_power" => SetMainPower(parameters),
                "set_bright" => SetIntegerProperty("bright", parameters),
                "set_ct_abx" => SetIntegerProperty("ct", parameters),
                "bg_set_power" => SetBackgroundPower(parameters),
                "bg_set_bright" => SetIntegerProperty("bg_bright", parameters),
                "bg_set_ct_abx" => SetIntegerProperty("bg_ct", parameters),
                "bg_set_rgb" => SetIntegerProperty("bg_rgb", parameters),
                "bg_set_scene" => SetBackgroundScene(parameters),
                _ => throw new NotSupportedException(method),
            };

            return Task.FromResult(result);
        }

        private string[] ReadProperties(
            IReadOnlyList<object?> parameters) =>
            parameters
                .Select(parameter =>
                {
                    string property = (string)parameter!;
                    if (property == "power" && AggregateOverride is not null)
                    {
                        return AggregateOverride;
                    }

                    return _state[property];
                })
                .ToArray();

        private IReadOnlyList<string> SetMainPower(
            IReadOnlyList<object?> parameters)
        {
            _state["main_power"] = (string)parameters[0]!;
            UpdateAggregatePower();
            return ["ok"];
        }

        private IReadOnlyList<string> SetIntegerProperty(
            string property,
            IReadOnlyList<object?> parameters)
        {
            _state[property] = ((int)parameters[0]!).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            return ["ok"];
        }

        private IReadOnlyList<string> SetBackgroundPower(
            IReadOnlyList<object?> parameters)
        {
            string requested = (string)parameters[0]!;
            if (requested == "off")
            {
                _state["bg_power"] = "off";
                UpdateAggregatePower();
            }
            else if (!BackgroundRendererSilent)
            {
                _state["bg_power"] = "on";
                UpdateAggregatePower();
            }

            return ["ok"];
        }

        private IReadOnlyList<string> SetBackgroundScene(
            IReadOnlyList<object?> parameters)
        {
            SceneRefreshCount++;
            if (SceneRecoveryWorks)
            {
                BackgroundRendererSilent = false;
                _state["bg_power"] = "on";
                _state["bg_rgb"] = ((int)parameters[1]!).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                _state["bg_bright"] = ((int)parameters[2]!).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                _state["bg_lmode"] = "1";

                if (SceneChangesMainPower)
                {
                    _state["main_power"] = "on";
                }

                UpdateAggregatePower();
            }

            return ["ok"];
        }

        private void UpdateAggregatePower()
        {
            _state["power"] =
                _state["main_power"] == "on" || _state["bg_power"] == "on"
                    ? "on"
                    : "off";
        }
    }
}
