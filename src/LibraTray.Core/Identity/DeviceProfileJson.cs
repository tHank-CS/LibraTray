using System.Text.Json;

namespace LibraTray.Core.Identity;

/// <summary>
/// Serializes the identity portion of a device profile. Product names and
/// hardware models are re-derived from the exact internal model when loading;
/// persisted derived values can therefore never create a false product match.
/// </summary>
public static class DeviceProfileJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string Serialize(DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var profile = new DeviceProfileData
        {
            FriendlyProductName = identity.FriendlyProductName,
            HardwareModel = identity.HardwareModel,
            InternalModel = identity.InternalModel,
            ReportedName = identity.ReportedName,
            UserAlias = identity.UserAlias,
        };

        return JsonSerializer.Serialize(profile, SerializerOptions);
    }

    /// <summary>
    /// Loads a profile or returns <paramref name="fallbackIdentity"/> unchanged
    /// when the profile is absent, malformed, or structurally incomplete.
    /// </summary>
    public static DeviceIdentity DeserializeOrDefault(
        string? json,
        DeviceIdentity fallbackIdentity)
    {
        ArgumentNullException.ThrowIfNull(fallbackIdentity);

        if (string.IsNullOrWhiteSpace(json))
        {
            return fallbackIdentity;
        }

        try
        {
            DeviceProfileData? profile =
                JsonSerializer.Deserialize<DeviceProfileData>(json, SerializerOptions);

            if (profile is null
                || (string.IsNullOrWhiteSpace(profile.InternalModel)
                    && string.IsNullOrWhiteSpace(profile.ReportedName)))
            {
                return fallbackIdentity;
            }

            DeviceIdentity resolved = ProductIdentityMapper.Resolve(
                profile.InternalModel,
                profile.ReportedName);

            return resolved.WithUserAlias(profile.UserAlias);
        }
        catch (JsonException)
        {
            return fallbackIdentity;
        }
        catch (NotSupportedException)
        {
            return fallbackIdentity;
        }
    }

    private sealed class DeviceProfileData
    {
        public string? FriendlyProductName { get; set; }

        public string? HardwareModel { get; set; }

        public string? InternalModel { get; set; }

        public string? ReportedName { get; set; }

        public string? UserAlias { get; set; }
    }
}
