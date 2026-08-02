using System.Text.Json;
using LibraTray.Core.Devices.LibraPro;

namespace LibraTray.Core.Automation;

public sealed record ShutdownRestoreTicket
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string DeviceKey { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    public LibraProTargetState? RestoreTarget { get; init; }
}

public interface IShutdownRestoreTicketStore
{
    ShutdownRestoreTicket? Load();

    void Save(ShutdownRestoreTicket ticket);

    void Clear();
}

public sealed class ShutdownRestoreTicketStore : IShutdownRestoreTicketStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public ShutdownRestoreTicketStore(string? ticketPath = null)
    {
        TicketPath = ticketPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "LibraTray",
            "shutdown-restore.json");
    }

    public string TicketPath { get; }

    public ShutdownRestoreTicket? Load()
    {
        try
        {
            if (!File.Exists(TicketPath))
            {
                return null;
            }

            string json = File.ReadAllText(TicketPath);
            ShutdownRestoreTicket? ticket =
                JsonSerializer.Deserialize<ShutdownRestoreTicket>(
                    json,
                    SerializerOptions);
            return IsStructurallyValid(ticket) ? ticket : null;
        }
        catch (Exception exception) when (
            exception is JsonException
                or NotSupportedException
                or IOException
                or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(ShutdownRestoreTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!IsStructurallyValid(ticket))
        {
            throw new ArgumentException(
                "The shutdown restore ticket is incomplete.",
                nameof(ticket));
        }

        string? directory = Path.GetDirectoryName(TicketPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The ticket path must include a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(TicketPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            string json = JsonSerializer.Serialize(ticket, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, TicketPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Clear()
    {
        if (File.Exists(TicketPath))
        {
            File.Delete(TicketPath);
        }
    }

    private static bool IsStructurallyValid(ShutdownRestoreTicket? ticket) =>
        ticket is
        {
            SchemaVersion: ShutdownRestoreTicket.CurrentSchemaVersion,
            DeviceKey.Length: > 0,
            RestoreTarget: not null,
        };
}
