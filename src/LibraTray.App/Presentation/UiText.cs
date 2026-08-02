using System.Globalization;
using System.Windows;

namespace LibraTray.App.Presentation;

internal static class UiText
{
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Application.Current?.TryFindResource(key) as string ?? key;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(
            CultureInfo.CurrentCulture,
            Get(key),
            arguments);
}
