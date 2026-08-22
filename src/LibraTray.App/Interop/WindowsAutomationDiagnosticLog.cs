using System.Globalization;
using System.IO;
using System.Text;

namespace LibraTray.App.Interop;

internal sealed class WindowsAutomationDiagnosticLog
{
    private const long MaximumBytes = 256 * 1024;
    private const int MaximumDetailLength = 512;
    private readonly object _sync = new();

    public WindowsAutomationDiagnosticLog(string? logPath = null)
    {
        LogPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LibraTray",
            "logs",
            "windows-automation.log");
    }

    public string LogPath { get; }

    public void Write(string category, string detail)
    {
        string safeCategory = Sanitize(category, 64);
        string safeDetail = Sanitize(detail, MaximumDetailLength);
        lock (_sync)
        {
            try
            {
                string directory = Path.GetDirectoryName(LogPath)
                    ?? throw new InvalidOperationException(
                        "The automation log path requires a parent directory.");
                Directory.CreateDirectory(directory);
                if (File.Exists(LogPath)
                    && new FileInfo(LogPath).Length >= MaximumBytes)
                {
                    File.Move(LogPath, LogPath + ".previous", overwrite: true);
                }

                File.AppendAllText(
                    LogPath,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{DateTimeOffset.UtcNow:O}\t{safeCategory}\t{safeDetail}{Environment.NewLine}"),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                // Diagnostics are best effort and must never affect automation.
            }
        }
    }

    private static string Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        foreach (char character in value)
        {
            if (builder.Length == maximumLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString();
    }
}
