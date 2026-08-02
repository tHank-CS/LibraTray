using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace LibraTray.App.Interop;

internal static class WindowsStartupRegistrationService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LibraTray";
    private const string ShortcutFileName = "LibraTray.lnk";

    public static void SetEnabled(bool enabled)
    {
        string startupDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Startup);
        string shortcutPath = Path.Combine(
            startupDirectory,
            ShortcutFileName);

        if (enabled)
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "The current executable path is unavailable.");
            CreateStartupShortcut(shortcutPath, executablePath);
            RemoveLegacyRunValue();
            return;
        }

        File.Delete(shortcutPath);
        RemoveLegacyRunValue();
    }

    private static void CreateStartupShortcut(
        string shortcutPath,
        string executablePath)
    {
        string? workingDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException(
                "The executable directory is unavailable.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        string temporaryPath = $"{shortcutPath}.{Guid.NewGuid():N}.tmp";
        object? shellLink = null;
        try
        {
            shellLink = new ShellLink();
            IShellLinkW link = (IShellLinkW)shellLink;
            link.SetPath(executablePath);
            link.SetArguments("--startup");
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription("Start LibraTray when signing in to Windows");
            link.SetIconLocation(executablePath, 0);
            ((IPersistFile)link).Save(temporaryPath, true);
            File.Move(temporaryPath, shortcutPath, overwrite: true);
        }
        catch (COMException exception)
        {
            throw new IOException(
                "Windows could not create the startup shortcut.",
                exception);
        }
        finally
        {
            if (shellLink is not null)
            {
                Marshal.FinalReleaseComObject(shellLink);
            }

            File.Delete(temporaryPath);
        }
    }

    private static void RemoveLegacyRunValue()
    {
        using RegistryKey? existing = Registry.CurrentUser.OpenSubKey(
            RunKeyPath,
            writable: true);
        existing?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out] StringBuilder file,
            int fileCharacterCount,
            nint findData,
            uint flags);

        void GetIdList(out nint itemIdList);

        void SetIdList(nint itemIdList);

        void GetDescription(
            [Out] StringBuilder description,
            int descriptionCharacterCount);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);

        void GetWorkingDirectory(
            [Out] StringBuilder directory,
            int directoryCharacterCount);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments(
            [Out] StringBuilder arguments,
            int argumentsCharacterCount);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCommand(out int showCommand);

        void SetShowCommand(int showCommand);

        void GetIconLocation(
            [Out] StringBuilder iconPath,
            int iconPathCharacterCount,
            out int iconIndex);

        void SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string iconPath,
            int iconIndex);

        void SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            uint reserved);

        void Resolve(nint ownerWindow, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
