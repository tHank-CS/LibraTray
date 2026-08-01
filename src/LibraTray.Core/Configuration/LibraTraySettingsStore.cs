using System.Text.Json;

namespace LibraTray.Core.Configuration;

public sealed class LibraTraySettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public LibraTraySettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "LibraTray",
            "settings.json");
    }

    public string SettingsPath { get; }

    public LibraTraySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new LibraTraySettings();
            }

            string json = File.ReadAllText(SettingsPath);
            LibraTraySettings? settings =
                JsonSerializer.Deserialize<LibraTraySettings>(
                    json,
                    SerializerOptions);
            return LibraTraySettingsNormalizer.Normalize(settings);
        }
        catch (JsonException)
        {
            return new LibraTraySettings();
        }
        catch (NotSupportedException)
        {
            return new LibraTraySettings();
        }
        catch (IOException)
        {
            return new LibraTraySettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new LibraTraySettings();
        }
    }

    public LibraTraySettings Save(LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LibraTraySettings normalized =
            LibraTraySettingsNormalizer.Normalize(settings);
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The settings path must include a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            string json = JsonSerializer.Serialize(normalized, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return normalized;
    }
}
