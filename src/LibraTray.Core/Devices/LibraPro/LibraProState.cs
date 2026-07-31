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
