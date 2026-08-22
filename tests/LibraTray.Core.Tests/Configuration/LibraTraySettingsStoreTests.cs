using LibraTray.Core.Configuration;
using LibraTray.Core.Devices.LibraPro;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Configuration;

[TestClass]
public sealed class LibraTraySettingsStoreTests
{
    private string _directory = null!;
    private string _settingsPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"LibraTraySettingsTests-{Guid.NewGuid():N}");
        _settingsPath = Path.Combine(_directory, "settings.json");
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
    public void SaveAndLoadRoundTripsSupportedSettings()
    {
        var store = new LibraTraySettingsStore(_settingsPath);
        Guid presetId = Guid.NewGuid();
        var settings = new LibraTraySettings
        {
            UserAlias = "Work light",
            BrightnessStep = 8,
            BackgroundBrightnessStep = 20,
            ColorTemperatureStep = 250,
            AllowExtremeColorTemperature = true,
            ExperimentalSegmentRgbEnabled = true,
            AdjustBrightnessWithTrayWheel = false,
            Theme = AppTheme.Dark,
            Language = AppLanguage.English,
            ShowOnScreenDisplay = false,
            WindowsAutomation = new WindowsAutomationSettings
            {
                LockAndUnlockEnabled = true,
                StartWithWindows = true,
                TurnOnLightsAfterWindowsStartup = true,
                ShutdownAndStartupEnabled = true,
                DisplayPowerEnabled = true,
                ManualSuppressionSeconds = 8,
            },
            Hotkeys = new GlobalHotkeySettings
            {
                ToggleMainPower = "Ctrl+Shift+L",
            },
            Presets =
            [
                new LibraProPreset
                {
                    Id = presetId,
                    Name = "Reading",
                    MainPower = true,
                    MainBrightness = 72,
                    MainColorTemperature = 4_500,
                    BackgroundPower = true,
                    BackgroundBrightness = 35,
                    BackgroundRgb = 0x3366CC,
                    AmbientColorMode = AmbientColorMode.Segmented,
                    SegmentRgb = new SegmentRgbRequest(0x112233, 0x445566),
                },
            ],
        };

        LibraTraySettings saved = store.Save(settings);
        LibraTraySettings loaded = store.Load();

        Assert.AreEqual(saved.UserAlias, loaded.UserAlias);
        Assert.AreEqual(saved.BrightnessStep, loaded.BrightnessStep);
        Assert.AreEqual(
            saved.BackgroundBrightnessStep,
            loaded.BackgroundBrightnessStep);
        Assert.AreEqual(
            saved.ColorTemperatureStep,
            loaded.ColorTemperatureStep);
        Assert.AreEqual(
            saved.AllowExtremeColorTemperature,
            loaded.AllowExtremeColorTemperature);
        Assert.IsTrue(loaded.ExperimentalSegmentRgbEnabled);
        Assert.AreEqual(
            saved.AdjustBrightnessWithTrayWheel,
            loaded.AdjustBrightnessWithTrayWheel);
        Assert.AreEqual(AppTheme.Dark, loaded.Theme);
        Assert.AreEqual(AppLanguage.English, loaded.Language);
        Assert.IsFalse(loaded.ShowOnScreenDisplay);
        Assert.AreEqual(saved.WindowsAutomation, loaded.WindowsAutomation);
        Assert.AreEqual(saved.Hotkeys, loaded.Hotkeys);
        CollectionAssert.AreEqual(
            saved.Presets.ToArray(),
            loaded.Presets.ToArray());
        Assert.AreEqual(presetId, loaded.Presets[0].Id);
        Assert.AreEqual("Ctrl+Shift+L", loaded.Hotkeys.ToggleMainPower);
        Assert.IsTrue(File.Exists(_settingsPath));
        Assert.IsEmpty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [TestMethod]
    public void LoadCorruptJsonReturnsCompleteDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_settingsPath, "{not-json");
        var store = new LibraTraySettingsStore(_settingsPath);

        LibraTraySettings loaded = store.Load();

        Assert.AreEqual(LibraTraySettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.IsNull(loaded.UserAlias);
        Assert.AreEqual(5, loaded.BrightnessStep);
        Assert.AreEqual(20, loaded.BackgroundBrightnessStep);
        Assert.AreEqual(100, loaded.ColorTemperatureStep);
        Assert.IsFalse(loaded.AllowExtremeColorTemperature);
        Assert.IsFalse(loaded.ExperimentalSegmentRgbEnabled);
        Assert.IsTrue(loaded.AdjustBrightnessWithTrayWheel);
        Assert.AreEqual(AppTheme.System, loaded.Theme);
        Assert.AreEqual(AppLanguage.System, loaded.Language);
        Assert.IsTrue(loaded.ShowOnScreenDisplay);
        Assert.IsFalse(loaded.WindowsAutomation.IsAnyEnabled);
        Assert.IsFalse(loaded.WindowsAutomation.StartWithWindows);
        Assert.IsFalse(
            loaded.WindowsAutomation.TurnOnLightsAfterWindowsStartup);
        Assert.AreEqual(5, loaded.WindowsAutomation.ManualSuppressionSeconds);
        Assert.AreEqual("Ctrl+Alt+L", loaded.Hotkeys.ToggleMainPower);
        Assert.IsEmpty(loaded.Presets);
    }

    [TestMethod]
    public void LoadNormalizesInvalidFieldsAndPresets()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _settingsPath,
            """
            {
              "SchemaVersion": 1,
              "UserAlias": "  Desk\n light  ",
              "BrightnessStep": 0,
              "BackgroundBrightnessStep": 101,
              "ColorTemperatureStep": 5000,
              "Theme": 999,
              "Language": 999,
              "WindowsAutomation": {
                "LockAndUnlockEnabled": true,
                "StartWithWindows": true,
                "TurnOnLightsAfterWindowsStartup": true,
                "ManualSuppressionSeconds": 61
              },
              "Hotkeys": { "ToggleMainPower": " " },
              "Presets": [
                { "Name": "Bad", "MainBrightness": 0 },
                {
                  "Name": "  Focus  ",
                  "MainBrightness": 60,
                  "MainColorTemperature": 5000,
                  "BackgroundBrightness": 40,
                  "BackgroundRgb": 255
                }
              ],
              "UnknownFutureField": true
            }
            """);
        var store = new LibraTraySettingsStore(_settingsPath);

        LibraTraySettings loaded = store.Load();

        Assert.AreEqual("Desk light", loaded.UserAlias);
        Assert.AreEqual(5, loaded.BrightnessStep);
        Assert.AreEqual(20, loaded.BackgroundBrightnessStep);
        Assert.AreEqual(100, loaded.ColorTemperatureStep);
        Assert.IsFalse(loaded.AllowExtremeColorTemperature);
        Assert.AreEqual(AppTheme.System, loaded.Theme);
        Assert.AreEqual(AppLanguage.System, loaded.Language);
        Assert.IsTrue(loaded.WindowsAutomation.LockAndUnlockEnabled);
        Assert.IsFalse(
            loaded.WindowsAutomation.TurnOnLightsAfterWindowsStartup);
        Assert.AreEqual(5, loaded.WindowsAutomation.ManualSuppressionSeconds);
        Assert.AreEqual("Ctrl+Alt+L", loaded.Hotkeys.ToggleMainPower);
        Assert.HasCount(1, loaded.Presets);
        Assert.AreEqual("Focus", loaded.Presets[0].Name);
    }

    [TestMethod]
    public void LoadSchemaOneAddsNewStepDefaultsAndMigratesToCurrentSchema()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _settingsPath,
            """{"SchemaVersion":1,"BrightnessStep":10,"ColorTemperatureStep":200}""");
        var store = new LibraTraySettingsStore(_settingsPath);

        LibraTraySettings loaded = store.Load();

        Assert.AreEqual(LibraTraySettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(10, loaded.BrightnessStep);
        Assert.AreEqual(20, loaded.BackgroundBrightnessStep);
        Assert.AreEqual(100, loaded.ColorTemperatureStep);
        Assert.IsFalse(loaded.AllowExtremeColorTemperature);
    }

    [TestMethod]
    public void LoadSchemaFourKeepsStartupPowerPolicyDisabled()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _settingsPath,
            """
            {
              "SchemaVersion": 4,
              "WindowsAutomation": {
                "StartWithWindows": true,
                "TurnOnLightsAfterWindowsStartup": true
              }
            }
            """);
        var store = new LibraTraySettingsStore(_settingsPath);

        LibraTraySettings loaded = store.Load();

        Assert.AreEqual(
            LibraTraySettings.CurrentSchemaVersion,
            loaded.SchemaVersion);
        Assert.IsTrue(loaded.WindowsAutomation.StartWithWindows);
        Assert.IsFalse(
            loaded.WindowsAutomation.TurnOnLightsAfterWindowsStartup);
    }

    [TestMethod]
    public void StartupPowerPolicyRequiresStartupRegistration()
    {
        var store = new LibraTraySettingsStore(_settingsPath);
        var settings = new LibraTraySettings
        {
            WindowsAutomation = new WindowsAutomationSettings
            {
                StartWithWindows = false,
                TurnOnLightsAfterWindowsStartup = true,
            },
        };

        LibraTraySettings saved = store.Save(settings);

        Assert.IsFalse(
            saved.WindowsAutomation.TurnOnLightsAfterWindowsStartup);
    }

    [TestMethod]
    public void LoadFutureSchemaReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _settingsPath,
            """{"SchemaVersion":999,"UserAlias":"Do not use"}""");
        var store = new LibraTraySettingsStore(_settingsPath);

        LibraTraySettings loaded = store.Load();

        Assert.IsNull(loaded.UserAlias);
        Assert.AreEqual(LibraTraySettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }
}
