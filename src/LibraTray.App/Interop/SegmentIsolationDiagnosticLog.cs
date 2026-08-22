using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using LibraTray.Core.Networking;

namespace LibraTray.App.Interop;

internal sealed class SegmentIsolationDiagnosticLog
{
    private const long MaximumBytes = 256 * 1024;
    private const int MaximumLineLength = 2048;
    private readonly object _sync = new();

    public SegmentIsolationDiagnosticLog(string? logPath = null)
    {
        LogPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LibraTray",
            "logs",
            "segment-isolation.log");
    }

    public string LogPath { get; }

    public void WriteMode() => WriteLine(
        $"{DateTimeOffset.UtcNow:O}\tmode\tautomatic_scene_recovery=disabled;"
        + "windows_automation_writes=disabled");

    public void Write(YeelightCommandTrace entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string outcome = entry.FailureType is null
            ? "ok"
            : $"failed:{Sanitize(entry.FailureType)}";
        string parameters = JsonSerializer.Serialize(entry.Parameters);
        string results = entry.Results is null
            ? "null"
            : JsonSerializer.Serialize(entry.Results);
        WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.StartedUtc:O}\tcommand\tseq={entry.Sequence};"
            + $"method={Sanitize(entry.Method)};params={parameters};"
            + $"result={results};outcome={outcome};"
            + $"elapsed_ms={entry.Elapsed.TotalMilliseconds:F1}"));
    }

    private void WriteLine(string line)
    {
        string bounded = line.Length <= MaximumLineLength
            ? line
            : line[..MaximumLineLength];
        lock (_sync)
        {
            try
            {
                string directory = Path.GetDirectoryName(LogPath)
                    ?? throw new InvalidOperationException(
                        "The diagnostic log path requires a parent directory.");
                Directory.CreateDirectory(directory);
                if (File.Exists(LogPath)
                    && new FileInfo(LogPath).Length >= MaximumBytes)
                {
                    File.Move(LogPath, LogPath + ".previous", overwrite: true);
                }

                File.AppendAllText(
                    LogPath,
                    bounded + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                // Diagnostics are best effort and must not affect control.
            }
        }
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, 128));
        foreach (char character in value.Take(128))
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString();
    }
}
