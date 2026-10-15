using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Jobs;

/// <summary>Lo necesario para volver a lanzar un trabajo interrumpido.</summary>
public sealed class CopyJobRecord
{
    public string Id { get; set; } = "";

    public List<string> Sources { get; set; } = [];

    public string Destination { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    public ConflictPolicy Conflict { get; set; }

    public bool Verify { get; set; }

    public long ChunkSize { get; set; }

    public int BlockSize { get; set; }

    public int QueueDepth { get; set; }

    public bool Move { get; set; }

    public long MaxBytesPerSecond { get; set; }

    public static CopyJobRecord New(IEnumerable<string> sources, string destination, CopyOptions options) => new()
    {
        Move = options.Move,
        MaxBytesPerSecond = options.MaxBytesPerSecond,
        Id = Guid.NewGuid().ToString("N")[..8],
        Sources = [.. sources],
        Destination = destination,
        CreatedUtc = DateTime.UtcNow,
        Conflict = options.Conflict,
        Verify = options.Verify,
        ChunkSize = options.ChunkSize,
        BlockSize = options.BlockSize,
        QueueDepth = options.WriteQueueDepth,
    };

    public CopyOptions ToOptions()
    {
        // El bloque se guarda junto al tamaño de confirmación: ambos deben seguir encajando al reanudar.
        var options = new CopyOptions { Conflict = Conflict, Verify = Verify, Move = Move, MaxBytesPerSecond = MaxBytesPerSecond };
        if (ChunkSize > 0 && BlockSize > 0)
        {
            options.ChunkSize = ChunkSize;
            options.BlockSize = BlockSize;
        }

        if (QueueDepth > 0)
        {
            options.ReadQueueDepth = options.WriteQueueDepth = QueueDepth;
        }

        return options;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(CopyJobRecord))]
internal sealed partial class JobJsonContext : JsonSerializerContext;

/// <summary>Trabajos pendientes en %LOCALAPPDATA%\MegaCopier\jobs. Se escriben de forma atómica.</summary>
public sealed class JobStore
{
    public JobStore(string? directory = null)
    {
        Directory = directory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MegaCopier", "jobs");
    }

    public string Directory { get; }

    public void Save(CopyJobRecord job)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = PathFor(job.Id);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, job, JobJsonContext.Default.CopyJobRecord);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    public CopyJobRecord? Get(string id)
    {
        string path = PathFor(id);
        return File.Exists(path) ? Read(path) : null;
    }

    public IReadOnlyList<CopyJobRecord> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(Read)
            .OfType<CopyJobRecord>()
            .OrderBy(j => j.CreatedUtc)
            .ToList();
    }

    public void Delete(string id)
    {
        string path = PathFor(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string id) => System.IO.Path.Combine(Directory, id + ".json");

    private static CopyJobRecord? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, JobJsonContext.Default.CopyJobRecord);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
