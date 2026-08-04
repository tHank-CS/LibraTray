using System.Text.Json;
using LibraTray.Core.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Configuration;

[TestClass]
public sealed class LibraTraySettingsTransferServiceTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "LibraTray.Tests",
            Guid.NewGuid().ToString("N"));
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
    public void JsonRoundTripPreservesNormalizedPreferences()
    {
        var settings = new LibraTraySettings
        {
            UserAlias = "Desk light",
            BrightnessStep = 8,
            BackgroundBrightnessStep = 20,
            AllowExtremeColorTemperature = true,
            Theme = AppTheme.Dark,
            Language = AppLanguage.English,
            ShowOnScreenDisplay = false,
            WindowsAutomation = new WindowsAutomationSettings
            {
                StartWithWindows = true,
                DisplayPowerEnabled = true,
            },
            Presets =
            [
                new LibraProPreset
                {
                    Name = "Focus",
                    MainPower = true,
                    MainBrightness = 60,
                    MainColorTemperature = 5_000,
                    BackgroundBrightness = 40,
                    BackgroundRgb = 255,
                },
            ],
        };

        string json = LibraTraySettingsTransferService.CreateExportJson(settings);
        LibraTraySettings imported =
            LibraTraySettingsTransferService.ImportJson(json);

        Assert.AreEqual("Desk light", imported.UserAlias);
        Assert.AreEqual(8, imported.BrightnessStep);
        Assert.AreEqual(20, imported.BackgroundBrightnessStep);
        Assert.IsTrue(imported.AllowExtremeColorTemperature);
        Assert.AreEqual(AppTheme.Dark, imported.Theme);
        Assert.AreEqual(AppLanguage.English, imported.Language);
        Assert.IsFalse(imported.ShowOnScreenDisplay);
        Assert.IsTrue(imported.WindowsAutomation.StartWithWindows);
        Assert.IsTrue(imported.WindowsAutomation.DisplayPowerEnabled);
        Assert.HasCount(1, imported.Presets);
        Assert.AreEqual("Focus", imported.Presets[0].Name);
        StringAssert.Contains(json, "LibraTray.Settings");
    }

    [TestMethod]
    public void FileExportUsesAtomicReplacementWithoutTemporaryFiles()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(
            _directory,
            "profile" + LibraTraySettingsTransferService.FileExtension);
        File.WriteAllText(path, "old contents");
        LibraTraySettingsTransferService.Export(
            path,
            new LibraTraySettings { UserAlias = "Exported" });
        LibraTraySettings imported = LibraTraySettingsTransferService.Import(path);

        Assert.AreEqual("Exported", imported.UserAlias);
        Assert.IsEmpty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [TestMethod]
    public void ImportRejectsWrongFormatAndFutureVersions()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => LibraTraySettingsTransferService.ImportJson(
                """{"Format":"Other","FormatVersion":1,"Settings":{}}"""));
        Assert.ThrowsExactly<InvalidDataException>(
            () => LibraTraySettingsTransferService.ImportJson(
                """{"Format":"LibraTray.Settings","FormatVersion":2,"Settings":{}}"""));
        Assert.ThrowsExactly<InvalidDataException>(
            () => LibraTraySettingsTransferService.ImportJson(
                """{"Format":"LibraTray.Settings","FormatVersion":1,"Settings":{"SchemaVersion":999}}"""));
    }

    [TestMethod]
    public void ImportRejectsMalformedOrOversizedInput()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => LibraTraySettingsTransferService.ImportJson("{not-json"));
        Assert.ThrowsExactly<InvalidDataException>(
            () => LibraTraySettingsTransferService.ImportJson(
                JsonSerializer.Serialize(
                    new
                    {
                        Format = "LibraTray.Settings",
                        FormatVersion = 1,
                        Padding = new string('x', 1024 * 1024),
                    })));
    }
}
