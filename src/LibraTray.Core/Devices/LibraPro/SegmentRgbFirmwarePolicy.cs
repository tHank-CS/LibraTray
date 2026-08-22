using LibraTray.Core.Identity;

namespace LibraTray.Core.Devices.LibraPro;

public enum SegmentRgbFirmwareAccess
{
    Unsupported,
    Allowed,
    RequiresFirmwareConfirmation,
    RequiresUnknownFirmwareConfirmation,
}

public static class SegmentRgbFirmwarePolicy
{
    public static SegmentRgbFirmwareAccess Evaluate(
        string internalModel,
        IEnumerable<string> capabilities,
        string? firmwareVersion,
        IEnumerable<string> acknowledgedFirmwareVersions,
        bool unknownFirmwareAcknowledgedForSession)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalModel);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(acknowledgedFirmwareVersions);
        if (!ProductIdentityMapper.IsSupportedInternalModel(internalModel)
            || !capabilities.Contains("set_segment_rgb", StringComparer.Ordinal))
        {
            return SegmentRgbFirmwareAccess.Unsupported;
        }

        string? firmware = string.IsNullOrWhiteSpace(firmwareVersion)
            ? null
            : firmwareVersion.Trim();
        if (string.Equals(firmware, "38", StringComparison.Ordinal)
            || (firmware is not null
                && acknowledgedFirmwareVersions.Contains(
                    firmware,
                    StringComparer.Ordinal))
            || (firmware is null && unknownFirmwareAcknowledgedForSession))
        {
            return SegmentRgbFirmwareAccess.Allowed;
        }

        return firmware is null
            ? SegmentRgbFirmwareAccess.RequiresUnknownFirmwareConfirmation
            : SegmentRgbFirmwareAccess.RequiresFirmwareConfirmation;
    }
}
