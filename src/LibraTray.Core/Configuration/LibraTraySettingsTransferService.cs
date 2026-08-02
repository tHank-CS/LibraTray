using System.Text;
using System.Text.Json;

namespace LibraTray.Core.Configuration;

public static class LibraTraySettingsTransferService
{
    public const string FileExtension = ".libratray-settings.json";

    private const string FormatName = "LibraTray.Settings";
    private const int CurrentFormatVersion = 1;
    private const int MaximumImportBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static void Export(string path, LibraTraySettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The export path must include a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                CreateExportJson(settings),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static LibraTraySettings Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The settings export does not exist.",
                fullPath);
        }

        if (file.Length <= 0 || file.Length > MaximumImportBytes)
        {
            throw new InvalidDataException(
                "The settings export is empty or exceeds the 1 MiB limit.");
        }

        string json;
        try
        {
            json = File.ReadAllText(
                fullPath,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "The settings export is not valid UTF-8.",
                exception);
        }

        return ImportJson(json);
    }

    public static string CreateExportJson(LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LibraTraySettings normalized =
            LibraTraySettingsNormalizer.Normalize(settings);
        var export = new SettingsExportEnvelope
        {
            Format = FormatName,
            FormatVersion = CurrentFormatVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Settings = normalized,
        };
        return JsonSerializer.Serialize(export, SerializerOptions);
    }

    public static LibraTraySettings ImportJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumImportBytes)
        {
            throw new InvalidDataException(
                "The settings export exceeds the 1 MiB limit.");
        }

        SettingsExportEnvelope? export;
        try
        {
            export = JsonSerializer.Deserialize<SettingsExportEnvelope>(
                json,
                SerializerOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException(
                "The settings export is not valid JSON.",
                exception);
        }

        if (export is null
            || !string.Equals(export.Format, FormatName, StringComparison.Ordinal)
            || export.FormatVersion != CurrentFormatVersion
            || export.Settings is null)
        {
            throw new InvalidDataException(
                "The file is not a supported LibraTray settings export.");
        }

        if (export.Settings.SchemaVersion < 1
            || export.Settings.SchemaVersion
                > LibraTraySettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                "The settings schema is not supported by this version.");
        }

        return LibraTraySettingsNormalizer.Normalize(export.Settings);
    }

    private sealed record SettingsExportEnvelope
    {
        public string? Format { get; init; }

        public int FormatVersion { get; init; }

        public DateTimeOffset ExportedAtUtc { get; init; }

        public LibraTraySettings? Settings { get; init; }
    }
}
