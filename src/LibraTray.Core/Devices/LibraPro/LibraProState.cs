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

public enum AmbientColorMode
{
    Whole,
    Segmented,
}

/// <summary>
/// A requested left/right appearance. The device exposes no readable segment
/// properties, so this value must never be treated as confirmed device state.
/// </summary>
public sealed record SegmentRgbRequest
{
    public SegmentRgbRequest(int leftRgb, int rightRgb)
    {
        ValidateRgb(leftRgb, nameof(leftRgb));
        ValidateRgb(rightRgb, nameof(rightRgb));
        LeftRgb = leftRgb;
        RightRgb = rightRgb;
    }

    public int LeftRgb { get; }

    public int RightRgb { get; }

    private static void ValidateRgb(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value,
            16_777_215,
            parameterName);
    }
}

public sealed record LibraProTargetState
{
    public LibraProTargetState(
        bool mainPower,
        int mainBrightness,
        int mainColorTemperature,
        bool backgroundPower,
        int backgroundBrightness,
        int backgroundRgb,
        AmbientColorMode ambientColorMode = AmbientColorMode.Whole,
        SegmentRgbRequest? segmentRgb = null)
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
        if (!Enum.IsDefined(ambientColorMode))
        {
            throw new ArgumentOutOfRangeException(nameof(ambientColorMode));
        }

        if (ambientColorMode == AmbientColorMode.Segmented && segmentRgb is null)
        {
            throw new ArgumentException(
                "Segmented targets require requested left and right colours.",
                nameof(segmentRgb));
        }

        MainPower = mainPower;
        MainBrightness = mainBrightness;
        MainColorTemperature = mainColorTemperature;
        BackgroundPower = backgroundPower;
        BackgroundBrightness = backgroundBrightness;
        BackgroundRgb = backgroundRgb;
        AmbientColorMode = ambientColorMode;
        SegmentRgb = segmentRgb;
    }

    public bool MainPower { get; }

    public int MainBrightness { get; }

    public int MainColorTemperature { get; }

    public bool BackgroundPower { get; }

    public int BackgroundBrightness { get; }

    public int BackgroundRgb { get; }

    public AmbientColorMode AmbientColorMode { get; }

    public SegmentRgbRequest? SegmentRgb { get; }
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

public enum SegmentRgbApplyStatus
{
    AcceptedUnverified,
}

public sealed record SegmentRgbApplyResult(
    LibraProState State,
    SegmentRgbRequest Request,
    SegmentRgbApplyStatus Status,
    long ConnectionEpoch);
