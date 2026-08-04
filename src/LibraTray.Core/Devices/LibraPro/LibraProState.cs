namespace LibraTray.Core.Devices.LibraPro;

/// <summary>
/// Confirmed readable state for the two independently controlled light
/// channels on a Yeelight Libra Pro.
/// </summary>
public sealed record LibraProState(
    bool AggregatePower,
    bool MainPower,
    bool BackgroundPower,
    int MainBrightness,
    int MainColorTemperature,
    int BackgroundBrightness,
    int BackgroundColorTemperature,
    int BackgroundRgb,
    int BackgroundHue,
    int BackgroundSaturation,
    int BackgroundLightMode);

public sealed record LibraProTargetState
{
    public LibraProTargetState(
        bool mainPower,
        int mainBrightness,
        int mainColorTemperature,
        bool backgroundPower,
        int backgroundBrightness,
        int backgroundRgb)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mainBrightness, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mainBrightness, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            mainColorTemperature,
            2_700);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            mainColorTemperature,
            6_500);
        ArgumentOutOfRangeException.ThrowIfLessThan(backgroundBrightness, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            backgroundBrightness,
            100);
        ArgumentOutOfRangeException.ThrowIfNegative(backgroundRgb);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            backgroundRgb,
            16_777_215);

        MainPower = mainPower;
        MainBrightness = mainBrightness;
        MainColorTemperature = mainColorTemperature;
        BackgroundPower = backgroundPower;
        BackgroundBrightness = backgroundBrightness;
        BackgroundRgb = backgroundRgb;
    }

    public bool MainPower { get; }

    public int MainBrightness { get; }

    public int MainColorTemperature { get; }

    public bool BackgroundPower { get; }

    public int BackgroundBrightness { get; }

    public int BackgroundRgb { get; }
}

/// <summary>
/// Last confirmed background appearance used to initialize the firmware's
/// background renderer after a verified cold-start failure.
/// </summary>
public sealed record LibraProBackgroundSnapshot
{
    public LibraProBackgroundSnapshot(int brightness, int rgb)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(brightness, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(brightness, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(rgb);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rgb, 16_777_215);

        Brightness = brightness;
        Rgb = rgb;
    }

    public int Brightness { get; }

    public int Rgb { get; }
}

public enum LibraProColdStartRecoveryMode
{
    Disabled,
    OnVerifiedFailure,
}

public sealed record LibraProAdapterOptions
{
    public LibraProColdStartRecoveryMode ColdStartRecoveryMode { get; init; } =
        LibraProColdStartRecoveryMode.OnVerifiedFailure;
}

public sealed record LibraProCommandResult(
    LibraProState State,
    bool RecoveryAttempted,
    long ConnectionEpoch);
