using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>Un trabajo terminado, para la página Historial.</summary>
public sealed class HistoryEntry
{
    public DateTime Finished { get; set; }

    public List<string> Sources { get; set; } = [];

    public string Destination { get; set; } = "";

    public bool Move { get; set; }

    public int Files { get; set; }

    public long Bytes { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public double Seconds { get; set; }

    public string State { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<HistoryEntry>))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext;

/// <summary>Historial de copias (los últimos 500 trabajos) en %LOCALAPPDATA%\MegaCopier\history.json.</summary>
internal static class History
{
    private const int MaxEntries = 500;
    private static readonly Lock Gate = new();

    private static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MegaCopier", "history.json");

    public static event Action? Changed;

    public static List<HistoryEntry> Load()
    {
        lock (Gate)
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), HistoryJsonContext.Default.ListHistoryEntry) ?? []
                    : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    public static void Add(CopyJob job)
    {
        var entry = new HistoryEntry
        {
            Finished = DateTime.Now,
            Sources = [.. job.Record.Sources],
            Destination = job.Record.Destination,
            Move = job.Record.Move,
            Files = job.Result?.FilesCopied ?? 0,
            Bytes = job.Result?.BytesTransferred ?? 0,
            Skipped = job.Result?.FilesSkipped ?? 0,
            Failed = job.Result?.Failures.Count ?? 0,
            Seconds = job.Result?.Elapsed.TotalSeconds ?? 0,
            State = job.State.ToString(),
        };

        lock (Gate)
        {
            var entries = Load();
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            Save(entries);
        }

        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Save([]);
        }

        Changed?.Invoke();
    }

    private static void Save(List<HistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, HistoryJsonContext.Default.ListHistoryEntry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
