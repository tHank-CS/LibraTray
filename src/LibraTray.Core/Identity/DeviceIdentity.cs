namespace LibraTray.Core.Identity;

/// <summary>
/// Keeps protocol identity, product identity, reported naming, and user naming
/// separate so that none of them can irreversibly overwrite another.
/// </summary>
public sealed record DeviceIdentity
{
    internal DeviceIdentity(
        string? friendlyProductName,
        string? hardwareModel,
        string? internalModel,
        string? reportedName,
        string? userAlias,
        bool isKnownProduct)
    {
        FriendlyProductName = NormalizeOptional(friendlyProductName);
        HardwareModel = NormalizeOptional(hardwareModel);
        InternalModel = PreserveOptional(internalModel);
        ReportedName = PreserveOptional(reportedName);
        UserAlias = NormalizeOptional(userAlias);
        IsKnownProduct = isKnownProduct;
    }

    public string? FriendlyProductName { get; }

    public string? HardwareModel { get; }

    public string? InternalModel { get; }

    public string? ReportedName { get; }

    public string? UserAlias { get; }

    public bool IsKnownProduct { get; }

    /// <summary>
    /// Gets the ordinary user-facing name. Protocol model identifiers are never
    /// used as the fallback display name.
    /// </summary>
    public string DisplayName =>
        UserAlias
        ?? FriendlyProductName
        ?? ReportedName?.Trim()
        ?? ProductIdentityCatalog.UnknownDeviceDisplayName;

    /// <summary>
    /// Returns a copy with a normalized user alias. Null, empty, or whitespace
    /// clears the alias and restores the default display name.
    /// </summary>
    public DeviceIdentity WithUserAlias(string? userAlias) =>
        new(
            FriendlyProductName,
            HardwareModel,
            InternalModel,
            ReportedName,
            userAlias,
            IsKnownProduct);

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? PreserveOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
