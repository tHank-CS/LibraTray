using System.Globalization;
using System.IO;
using System.Windows;
using LibraTray.Core.Configuration;
using Microsoft.Win32;

namespace LibraTray.App.Presentation;

internal static class AppearanceManager
{
    private const string ThemeResourcePrefix =
        "/LibraTray;component/Resources/Themes/";
    private const string LanguageResourcePrefix =
        "/LibraTray;component/Resources/Strings.";

    public static void Apply(Application application, LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(settings);

        AppLanguage language = ResolveLanguage(settings.Language);
        CultureInfo culture = language == AppLanguage.ChineseSimplified
            ? CultureInfo.GetCultureInfo("zh-CN")
            : CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = culture;

        ReplaceResourceDictionary(
            application,
            LanguageResourcePrefix,
            $"{LanguageResourcePrefix}{culture.Name}.xaml");

        AppTheme theme = settings.Theme == AppTheme.System
            ? ResolveSystemTheme()
            : settings.Theme;
        ReplaceResourceDictionary(
            application,
            ThemeResourcePrefix,
            $"{ThemeResourcePrefix}{theme}.xaml");
    }

    private static AppLanguage ResolveLanguage(AppLanguage language)
    {
        if (language != AppLanguage.System)
        {
            return language;
        }

        return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.ChineseSimplified
            : AppLanguage.English;
    }

    private static AppTheme ResolveSystemTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0
                ? AppTheme.Dark
                : AppTheme.Light;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            return AppTheme.Light;
        }
    }

    private static void ReplaceResourceDictionary(
        Application application,
        string sourcePrefix,
        string source)
    {
        var dictionaries = application.Resources.MergedDictionaries;
        ResourceDictionary? existing = dictionaries.FirstOrDefault(
            dictionary => dictionary.Source?.OriginalString.StartsWith(
                sourcePrefix,
                StringComparison.OrdinalIgnoreCase) == true);
        if (existing?.Source?.OriginalString.Equals(
                source,
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        if (existing is not null)
        {
            _ = dictionaries.Remove(existing);
        }

        dictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(source, UriKind.Relative),
            });
    }
}
