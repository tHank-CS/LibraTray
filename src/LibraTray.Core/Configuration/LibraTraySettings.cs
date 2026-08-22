using System.Text;
using LibraTray.Core.Devices.LibraPro;

namespace LibraTray.Core.Configuration;

public sealed record LibraTraySettings
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? UserAlias { get; init; }

    public int BrightnessStep { get; init; } = 5;

    public int BackgroundBrightnessStep { get; init; } = 20;

    public int ColorTemperatureStep { get; init; } = 100;

    public bool AllowExtremeColorTemperature { get; init; }

    public bool ExperimentalSegmentRgbEnabled { get; init; }

    public bool AdjustBrightnessWithTrayWheel { get; init; } = true;

    public AppTheme Theme { get; init; } = AppTheme.System;

    public AppLanguage Language { get; init; } = AppLanguage.System;

    public bool ShowOnScreenDisplay { get; init; } = true;

    public WindowsAutomationSettings WindowsAutomation { get; init; } = new();

    public GlobalHotkeySettings Hotkeys { get; init; } = new();

    public IReadOnlyList<LibraProPreset> Presets { get; init; } = [];
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum AppLanguage
{
    System,
    ChineseSimplified,
    English,
}

public sealed record WindowsAutomationSettings
{
    public bool LockAndUnlockEnabled { get; init; }

    public bool StartWithWindows { get; init; }

    public bool TurnOnLightsAfterWindowsStartup { get; init; }

    public bool ShutdownAndStartupEnabled { get; init; }

    public bool DisplayPowerEnabled { get; init; }

    public int ManualSuppressionSeconds { get; init; } = 5;

    public bool IsAnyEnabled =>
        LockAndUnlockEnabled
        || ShutdownAndStartupEnabled
        || DisplayPowerEnabled;

    public bool RequiresLifecycleEvents =>
        LockAndUnlockEnabled || DisplayPowerEnabled;
}

public sealed record GlobalHotkeySettings
{
    public string ToggleMainPower { get; init; } = "Ctrl+Alt+L";

    public string ToggleBackgroundPower { get; init; } = "Ctrl+Alt+A";

    public string IncreaseMainBrightness { get; init; } = "Ctrl+Alt+Up";

    public string DecreaseMainBrightness { get; init; } = "Ctrl+Alt+Down";

    public string IncreaseMainColorTemperature { get; init; } =
        "Ctrl+Alt+Right";

    public string DecreaseMainColorTemperature { get; init; } =
        "Ctrl+Alt+Left";
}

public sealed record LibraProPreset
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "Preset";

    public bool MainPower { get; init; }

    public int MainBrightness { get; init; } = 50;

    public int MainColorTemperature { get; init; } = 4_000;

    public bool BackgroundPower { get; init; }

    public int BackgroundBrightness { get; init; } = 50;

    public int BackgroundRgb { get; init; } = 13_395_711;

    public AmbientColorMode AmbientColorMode { get; init; }

    public SegmentRgbRequest? SegmentRgb { get; init; }

    public LibraProTargetState ToTargetState() =>
        new(
            MainPower,
            MainBrightness,
            MainColorTemperature,
            BackgroundPower,
            BackgroundBrightness,
            BackgroundRgb,
            AmbientColorMode,
            SegmentRgb);
}

internal static class LibraTraySettingsNormalizer
{
    private const int MaximumAliasLength = 128;
    private const int MaximumGestureLength = 64;
    private const int MaximumPresetCount = 20;
    private const int MaximumPresetNameLength = 64;

    public static LibraTraySettings Normalize(LibraTraySettings? settings)
    {
        var defaults = new LibraTraySettings();
        if (settings is null
            || settings.SchemaVersion < 1
            || settings.SchemaVersion > LibraTraySettings.CurrentSchemaVersion)
        {
            return defaults;
        }

        return settings with
        {
            SchemaVersion = LibraTraySettings.CurrentSchemaVersion,
            UserAlias = NormalizeDisplayText(
                settings.UserAlias,
                MaximumAliasLength),
            BrightnessStep = IsInRange(settings.BrightnessStep, 1, 25)
                ? settings.BrightnessStep
                : defaults.BrightnessStep,
            BackgroundBrightnessStep = IsInRange(
                settings.BackgroundBrightnessStep,
                1,
                100)
                    ? settings.BackgroundBrightnessStep
                    : defaults.BackgroundBrightnessStep,
            ColorTemperatureStep =
                settings.SchemaVersion == 1
                    && settings.ColorTemperatureStep == 200
                    ? defaults.ColorTemperatureStep
                : IsInRange(settings.ColorTemperatureStep, 50, 1_000)
                    ? settings.ColorTemperatureStep
                    : defaults.ColorTemperatureStep,
            AllowExtremeColorTemperature =
                settings.AllowExtremeColorTemperature,
            ExperimentalSegmentRgbEnabled =
                settings.ExperimentalSegmentRgbEnabled,
            AdjustBrightnessWithTrayWheel =
                settings.AdjustBrightnessWithTrayWheel,
            Theme = Enum.IsDefined(settings.Theme)
                ? settings.Theme
                : defaults.Theme,
            Language = Enum.IsDefined(settings.Language)
                ? settings.Language
                : defaults.Language,
            ShowOnScreenDisplay = settings.ShowOnScreenDisplay,
            WindowsAutomation = NormalizeWindowsAutomation(
                settings.WindowsAutomation,
                defaults.WindowsAutomation,
                settings.SchemaVersion),
            Hotkeys = NormalizeHotkeys(settings.Hotkeys, defaults.Hotkeys),
            Presets = NormalizePresets(settings.Presets),
        };
    }

    private static WindowsAutomationSettings NormalizeWindowsAutomation(
        WindowsAutomationSettings? settings,
        WindowsAutomationSettings defaults,
        int sourceSchemaVersion)
    {
        settings ??= defaults;
        return settings with
        {
            TurnOnLightsAfterWindowsStartup =
                sourceSchemaVersion >= 5
                && settings.StartWithWindows
                && settings.TurnOnLightsAfterWindowsStartup,
            ManualSuppressionSeconds = IsInRange(
                settings.ManualSuppressionSeconds,
                0,
                60)
                    ? settings.ManualSuppressionSeconds
                    : defaults.ManualSuppressionSeconds,
        };
    }

    private static GlobalHotkeySettings NormalizeHotkeys(
        GlobalHotkeySettings? settings,
        GlobalHotkeySettings defaults)
    {
        settings ??= defaults;
        return new GlobalHotkeySettings
        {
            ToggleMainPower = NormalizeGesture(
                settings.ToggleMainPower,
                defaults.ToggleMainPower),
            ToggleBackgroundPower = NormalizeGesture(
                settings.ToggleBackgroundPower,
                defaults.ToggleBackgroundPower),
            IncreaseMainBrightness = NormalizeGesture(
                settings.IncreaseMainBrightness,
                defaults.IncreaseMainBrightness),
            DecreaseMainBrightness = NormalizeGesture(
                settings.DecreaseMainBrightness,
                defaults.DecreaseMainBrightness),
            IncreaseMainColorTemperature = NormalizeGesture(
                settings.IncreaseMainColorTemperature,
                defaults.IncreaseMainColorTemperature),
            DecreaseMainColorTemperature = NormalizeGesture(
                settings.DecreaseMainColorTemperature,
                defaults.DecreaseMainColorTemperature),
        };
    }

    private static List<LibraProPreset> NormalizePresets(
        IReadOnlyList<LibraProPreset>? presets)
    {
        if (presets is null)
        {
            return [];
        }

        var normalized = new List<LibraProPreset>(
            Math.Min(presets.Count, MaximumPresetCount));
        var ids = new HashSet<Guid>();
        foreach (LibraProPreset? preset in presets)
        {
            string? name = NormalizeDisplayText(
                preset?.Name,
                MaximumPresetNameLength);
            if (preset is null
                || name is null
                || !IsInRange(preset.MainBrightness, 1, 100)
                || !IsInRange(preset.MainColorTemperature, 2_700, 6_500)
                || !IsInRange(preset.BackgroundBrightness, 1, 100)
                || !IsInRange(preset.BackgroundRgb, 0, 16_777_215)
                || !Enum.IsDefined(preset.AmbientColorMode)
                || (preset.AmbientColorMode == AmbientColorMode.Segmented
                    && preset.SegmentRgb is null))
            {
                continue;
            }

            Guid id = preset.Id == Guid.Empty || !ids.Add(preset.Id)
                ? Guid.NewGuid()
                : preset.Id;
            _ = ids.Add(id);
            normalized.Add(preset with { Id = id, Name = name });
            if (normalized.Count == MaximumPresetCount)
            {
                break;
            }
        }

        return normalized;
    }

    private static string NormalizeGesture(string? value, string fallback)
    {
        string? normalized = NormalizeDisplayText(value, MaximumGestureLength);
        return normalized ?? fallback;
    }

    private static string? NormalizeDisplayText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        bool pendingSpace = false;
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            int separatorLength = pendingSpace ? 1 : 0;
            if (builder.Length + separatorLength + rune.Utf16SequenceLength
                > maximumLength)
            {
                break;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static bool IsInRange(int value, int minimum, int maximum) =>
        value >= minimum && value <= maximum;
}
