using Microsoft.Win32;

namespace LibraTray.App.Interop;

internal static class WindowsStartupRegistrationService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LibraTray";

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "The current executable path is unavailable.");
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true);
            key.SetValue(
                ValueName,
                BuildCommand(executablePath),
                RegistryValueKind.String);
            return;
        }

        using RegistryKey? existing = Registry.CurrentUser.OpenSubKey(
            RunKeyPath,
            writable: true);
        existing?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    internal static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath.Replace("\"", "\\\"")}\" --startup";
    }
}
