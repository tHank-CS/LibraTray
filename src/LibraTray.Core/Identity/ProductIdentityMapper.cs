namespace LibraTray.Core.Identity;

/// <summary>
/// Resolves only exact protocol model mappings. Sales names, substrings, and
/// hardware-model guesses are deliberately not accepted as device identity.
/// </summary>
public static class ProductIdentityMapper
{
    public static DeviceIdentity Resolve(string? internalModel, string? reportedName = null)
    {
        string? normalizedModel = NormalizeOptional(internalModel);

        if (string.Equals(
                normalizedModel,
                ProductIdentityCatalog.LibraProInternalModel,
                StringComparison.OrdinalIgnoreCase))
        {
            return new DeviceIdentity(
                ProductIdentityCatalog.LibraProFriendlyProductName,
                ProductIdentityCatalog.LibraProHardwareModel,
                internalModel,
                reportedName,
                userAlias: null,
                isKnownProduct: true);
        }

        return new DeviceIdentity(
            friendlyProductName: null,
            hardwareModel: null,
            internalModel,
            reportedName,
            userAlias: null,
            isKnownProduct: false);
    }

    public static bool IsSupportedInternalModel(string? internalModel)
    {
        string? normalizedModel = NormalizeOptional(internalModel);
        return string.Equals(
            normalizedModel,
            ProductIdentityCatalog.LibraProInternalModel,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
