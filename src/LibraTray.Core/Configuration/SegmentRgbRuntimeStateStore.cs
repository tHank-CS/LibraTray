using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibraTray.Core.Devices.LibraPro;

namespace LibraTray.Core.Configuration;

public sealed record SegmentRgbRuntimeState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string DeviceKey { get; init; } = string.Empty;

    public AmbientColorMode LastColorMode { get; init; }

    public SegmentRgbRequest? LastSegmentRequest { get; init; }

    public IReadOnlyList<string> AcknowledgedFirmwareVersions { get; init; } = [];

    public DateTimeOffset UpdatedUtc { get; init; }
}

public sealed class SegmentRgbRuntimeStateStore
{
    private const int MaximumFirmwareCount = 32;
    private const int MaximumFirmwareLength = 64;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public SegmentRgbRuntimeStateStore(string? statePath = null)
    {
        StatePath = statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LibraTray",
            "segment-rgb-state.json");
    }

    public string StatePath { get; }

    public SegmentRgbRuntimeState? Load()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return null;
            }

            SegmentRgbRuntimeState? state = JsonSerializer.Deserialize<SegmentRgbRuntimeState>(
                File.ReadAllText(StatePath),
                SerializerOptions);
            return Normalize(state);
        }
        catch (Exception exception) when (
            exception is JsonException
                or NotSupportedException
                or IOException
                or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public SegmentRgbRuntimeState Save(SegmentRgbRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SegmentRgbRuntimeState normalized = Normalize(state)
            ?? throw new ArgumentException("The segment RGB state is invalid.", nameof(state));
        string directory = Path.GetDirectoryName(StatePath)
            ?? throw new InvalidOperationException("The state path requires a parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(StatePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(normalized, SerializerOptions));
            File.Move(temporaryPath, StatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return normalized;
    }

    public static string? CreateDeviceKey(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            deviceId.Trim().ToUpperInvariant())));
    }

    private static SegmentRgbRuntimeState? Normalize(SegmentRgbRuntimeState? state)
    {
        if (state is null
            || state.SchemaVersion != SegmentRgbRuntimeState.CurrentSchemaVersion
            || state.DeviceKey.Length != 64
            || !state.DeviceKey.All(Uri.IsHexDigit)
            || !Enum.IsDefined(state.LastColorMode)
            || (state.LastColorMode == AmbientColorMode.Segmented
                && state.LastSegmentRequest is null))
        {
            return null;
        }

        string[] firmwareVersions = (state.AcknowledgedFirmwareVersions ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => value.Length <= MaximumFirmwareLength
                && value.All(character => !char.IsControl(character)))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumFirmwareCount)
            .ToArray();
        return state with
        {
            SchemaVersion = SegmentRgbRuntimeState.CurrentSchemaVersion,
            DeviceKey = state.DeviceKey.ToUpperInvariant(),
            AcknowledgedFirmwareVersions = firmwareVersions,
        };
    }
}
