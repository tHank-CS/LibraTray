using System.Collections.ObjectModel;

namespace LibraTray.Core.Identity;

/// <summary>
/// Product identity facts that have an explicit, verified mapping.
/// </summary>
public static class ProductIdentityCatalog
{
    public const string LibraProFriendlyProductName = "Yeelight Libra Pro";
    public const string LibraProHardwareModel = "YLTD003";
    public const string LibraProInternalModel = "lamp15";
    public const string UnknownDeviceDisplayName = "Unknown Yeelight device";

    private static readonly ReadOnlyCollection<string> LibraProAliasValues = Array.AsReadOnly(
    [
        "Yeelight LED Screen Light Bar Pro",
        "Yeelight Monitor Light Bar Pro",
        "Yeelight Screen Light Bar Pro",
    ]);

    /// <summary>
    /// Gets known sales-channel names for informational display only.
    /// These values must never be used to identify a device.
    /// </summary>
    public static IReadOnlyList<string> LibraProAliases => LibraProAliasValues;
}
