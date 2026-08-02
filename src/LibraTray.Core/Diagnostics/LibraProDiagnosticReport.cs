using System.Globalization;
using System.Text;
using LibraTray.Core.Devices.LibraPro;
using LibraTray.Core.Identity;

namespace LibraTray.Core.Diagnostics;

public static class LibraProDiagnosticReport
{
    private const int MaximumCapabilities = 64;

    public static string Create(
        string applicationVersion,
        LibraProSessionStatus sessionStatus,
        DeviceIdentity? identity,
        LibraProConnectionInfo? connectionInfo,
        LibraProState? state,
        DateTimeOffset generatedUtc)
    {
        var report = new StringBuilder();
        _ = report.AppendLine("LibraTray diagnostic summary");
        Append(report, "GeneratedUtc", generatedUtc.ToUniversalTime().ToString("O"));
        Append(report, "ApplicationVersion", Sanitize(applicationVersion, 64));
        Append(report, "SessionStatus", sessionStatus.ToString());
        Append(
            report,
            "FriendlyProductName",
            Sanitize(identity?.FriendlyProductName, 128, "not-available"));
        Append(
            report,
            "HardwareModel",
            Sanitize(identity?.HardwareModel, 64, "not-available"));
        Append(
            report,
            "InternalModel",
            Sanitize(
                connectionInfo?.InternalModel ?? identity?.InternalModel,
                64,
                "not-available"));
        Append(
            report,
            "ReportedName",
            identity?.ReportedName is null ? "not-provided" : "[REDACTED]");
        Append(
            report,
            "UserAlias",
            identity?.UserAlias is null ? "not-configured" : "[REDACTED]");
        Append(
            report,
            "DeviceId",
            connectionInfo?.DeviceId is null
                ? "not-available"
                : "[REDACTED-DEVICE-ID]");
        Append(
            report,
            "ControlEndpoint",
            connectionInfo is null
                ? "not-available"
                : "[REDACTED-LAN-ENDPOINT]");
        Append(
            report,
            "FirmwareVersion",
            Sanitize(connectionInfo?.FirmwareVersion, 64, "not-available"));
        Append(
            report,
            "Capabilities",
            connectionInfo is null
                ? "not-available"
                : FormatCapabilities(connectionInfo.Capabilities));

        if (state is null)
        {
            Append(report, "ConfirmedState", "not-available");
        }
        else
        {
            Append(report, "AggregatePower", OnOff(state.AggregatePower));
            Append(report, "MainPower", OnOff(state.MainPower));
            Append(
                report,
                "MainBrightness",
                state.MainBrightness.ToString(CultureInfo.InvariantCulture));
            Append(
                report,
                "MainColorTemperature",
                state.MainColorTemperature.ToString(
                    CultureInfo.InvariantCulture));
            Append(report, "BackgroundPower", OnOff(state.BackgroundPower));
            Append(
                report,
                "BackgroundBrightness",
                state.BackgroundBrightness.ToString(
                    CultureInfo.InvariantCulture));
            Append(
                report,
                "BackgroundRgb",
                $"#{state.BackgroundRgb:X6}");
            Append(
                report,
                "BackgroundLightMode",
                state.BackgroundLightMode.ToString(
                    CultureInfo.InvariantCulture));
        }

        return report.ToString();
    }

    private static string FormatCapabilities(
        IReadOnlyList<string> capabilities)
    {
        string[] safe = capabilities
            .Take(MaximumCapabilities)
            .Select(value => Sanitize(value, 64))
            .ToArray();
        string suffix = capabilities.Count > MaximumCapabilities
            ? $", ... (+{capabilities.Count - MaximumCapabilities})"
            : string.Empty;
        return safe.Length == 0
            ? "none"
            : string.Join(", ", safe) + suffix;
    }

    private static string Sanitize(
        string? value,
        int maximumLength,
        string fallback = "invalid")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var result = new StringBuilder(Math.Min(value.Length, maximumLength));
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                continue;
            }

            if (result.Length + rune.Utf16SequenceLength > maximumLength)
            {
                break;
            }

            result.Append(rune);
        }

        return result.Length == 0 ? fallback : result.ToString();
    }

    private static string OnOff(bool enabled) => enabled ? "on" : "off";

    private static void Append(
        StringBuilder builder,
        string name,
        string value) =>
        _ = builder.Append(name).Append('=').AppendLine(value);
}
