using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>Un trabajo terminado, para la página Historial.</summary>
public sealed class HistoryEntry
{
    /// <summary>Identifica la lista de archivos guardada aparte (para "Deshacer").</summary>
    public string Id { get; set; } = "";

    public DateTime Finished { get; set; }

    public List<string> Sources { get; set; } = [];

    public string Destination { get; set; } = "";

    public bool Move { get; set; }

    public JobOperation Operation { get; set; }

    public int Files { get; set; }

    public long Bytes { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public double Seconds { get; set; }

    public string State { get; set; } = "";

    /// <summary>Ya se deshizo (no se puede deshacer dos veces).</summary>
    public bool Undone { get; set; }
}

/// <summary>Un archivo que copió el trabajo: dónde quedó y cómo era al terminar (para no borrar algo que cambió después).</summary>
public readonly record struct HistoryFile(string Source, string Destination, long Length, DateTime LastWriteUtc, bool Replaced);

[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<HistoryEntry>))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext;

/// <summary>
/// Historial de copias (los últimos 500 trabajos) en %LOCALAPPDATA%\MegaCopier\history.json; la lista de archivos de
/// cada copia va aparte, en history\&lt;id&gt;.tsv.
/// </summary>
internal static class History
{
    private const int MaxEntries = 500;
    private static readonly Lock Gate = new();

    private static string FilePath { get; } = Path.Combine(
        Engine.DataFolder.Path, "history.json");

    private static string ListsFolder { get; } = Path.Combine(Path.GetDirectoryName(FilePath)!, "history");

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

    /// <summary>Apunta un trabajo terminado. Devuelve el identificador de su entrada.</summary>
    public static string Add(CopyJob job)
    {
        var entry = new HistoryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Finished = DateTime.Now,
            Sources = [.. job.Record.Sources],
            Destination = job.Record.Destination,
            Move = job.Record.Move,
            Operation = job.Record.Operation,
            Files = job.Result?.FilesCopied ?? 0,
            Bytes = job.Result?.BytesTransferred ?? 0,
            Skipped = job.Result?.FilesSkipped ?? 0,
            Failed = job.Result?.Failures.Count ?? 0,
            Seconds = job.Result?.Elapsed.TotalSeconds ?? 0,
            State = job.State.ToString(),
        };

        // La lista de archivos copiados (solo copias de verdad, no simulaciones) se escribe sin bloquear la ventana.
        var copied = job.Record.Operation == JobOperation.Copy && !job.Record.DryRun && job.Files is { } files
            ? files.Snapshot().Where(i => i.State is FileState.Copied or FileState.Verified).ToList()
            : [];
        if (copied.Count > 0)
        {
            _ = Task.Run(() =>
            {
                SaveFiles(entry.Id, copied);
                Changed?.Invoke(); // ya se puede deshacer
            });
        }

        lock (Gate)
        {
            var entries = Load();
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
            {
                foreach (var old in entries.Skip(MaxEntries))
                {
                    TryDelete(ListPath(old.Id));
                }

                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            Save(entries);
        }

        Changed?.Invoke();
        return entry.Id;
    }

    /// <summary>Borra una entrada (y su lista de archivos copiados).</summary>
    public static void Remove(string id)
    {
        lock (Gate)
        {
            var entries = Load();
            if (entries.RemoveAll(e => e.Id == id) == 0)
            {
                return;
            }

            Save(entries);
            TryDelete(ListPath(id));
        }

        Changed?.Invoke();
    }

    public static void MarkUndone(string id)
    {
        lock (Gate)
        {
            var entries = Load();
            if (entries.FirstOrDefault(e => e.Id == id) is { } entry)
            {
                entry.Undone = true;
                Save(entries);
            }
        }

        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Save([]);
            try
            {
                if (Directory.Exists(ListsFolder))
                {
                    Directory.Delete(ListsFolder, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        Changed?.Invoke();
    }

    public static bool HasFiles(string id) => id.Length > 0 && File.Exists(ListPath(id));

    /// <summary>Los archivos que copió un trabajo (vacío si no se guardaron).</summary>
    public static List<HistoryFile> Files(string id)
    {
        var list = new List<HistoryFile>();
        string path = ListPath(id);
        if (id.Length == 0 || !File.Exists(path))
        {
            return list;
        }

        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            string[] parts = line.Split('\t');
            if (parts.Length == 5 && long.TryParse(parts[2], out long length) && long.TryParse(parts[3], out long ticks))
            {
                list.Add(new HistoryFile(parts[0], parts[1], length, new DateTime(ticks, DateTimeKind.Utc), parts[4] == "1"));
            }
        }

        return list;
    }

    private static void SaveFiles(string id, List<TransferItem> items)
    {
        try
        {
            Directory.CreateDirectory(ListsFolder);
            using var writer = new StreamWriter(ListPath(id), append: false, new UTF8Encoding(false));
            foreach (var item in items)
            {
                var info = new FileInfo(item.Destination);
                if (info.Exists)
                {
                    bool replaced = item.Action == PlannedAction.Replace;
                    writer.WriteLine($"{item.Source}\t{item.Destination}\t{info.Length}\t{info.LastWriteTimeUtc.Ticks}\t{(replaced ? 1 : 0)}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ListPath(string id) => Path.Combine(ListsFolder, id + ".tsv");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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
