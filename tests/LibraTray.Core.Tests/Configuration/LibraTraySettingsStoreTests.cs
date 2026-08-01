using LibraTray.Core.Configuration;
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
            ColorTemperatureStep = 250,
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
                },
            ],
        };

        LibraTraySettings saved = store.Save(settings);
        LibraTraySettings loaded = store.Load();

        Assert.AreEqual(saved.UserAlias, loaded.UserAlias);
        Assert.AreEqual(saved.BrightnessStep, loaded.BrightnessStep);
        Assert.AreEqual(
            saved.ColorTemperatureStep,
            loaded.ColorTemperatureStep);
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
        Assert.AreEqual(200, loaded.ColorTemperatureStep);
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
              "ColorTemperatureStep": 5000,
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
        Assert.AreEqual(200, loaded.ColorTemperatureStep);
        Assert.AreEqual("Ctrl+Alt+L", loaded.Hotkeys.ToggleMainPower);
        Assert.HasCount(1, loaded.Presets);
        Assert.AreEqual("Focus", loaded.Presets[0].Name);
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
