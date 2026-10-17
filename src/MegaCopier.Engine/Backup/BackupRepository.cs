using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Backup;

/// <summary>Un archivo dentro de una instantánea: sus metadatos y la lista de trozos que lo forman.</summary>
public sealed class SnapshotFile
{
    public string Path { get; set; } = "";

    public long Size { get; set; }

    public DateTime ModifiedUtc { get; set; }

    public FileAttributes Attributes { get; set; }

    public List<string> Chunks { get; set; } = [];
}

/// <summary>Una copia de seguridad hecha en un momento: qué carpetas, cuándo y qué archivos había.</summary>
public sealed class Snapshot
{
    public string Id { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    public List<string> Sources { get; set; } = [];

    public List<string> Folders { get; set; } = [];

    public List<SnapshotFile> Files { get; set; } = [];

    [JsonIgnore]
    public long TotalBytes => Files.Sum(f => f.Size);
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Snapshot))]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext;

/// <summary>
/// Repositorio de copias de seguridad sin duplicados (como restic o Borg, pero en una carpeta normal): cada trozo de
/// contenido se guarda una sola vez (comprimido y con su SHA-256 como nombre) y cada copia es una instantánea que solo
/// dice qué trozos forman cada archivo. Diez copias de algo que apenas cambia ocupan poco más que una, y se puede volver
/// a cualquiera. Estructura: &lt;repositorio&gt;\chunks\ab\abcd…  y  &lt;repositorio&gt;\snapshots\aaaa-mm-dd hhmmss.json.
/// </summary>
public sealed class BackupRepository(string root)
{
    public const string MarkerFile = "megacopier-backup.txt";

    public string Root { get; } = Path.GetFullPath(root);

    private string ChunksFolder => Path.Combine(Root, "chunks");

    private string SnapshotsFolder => Path.Combine(Root, "snapshots");

    public static bool IsRepository(string folder) => File.Exists(Path.Combine(folder, MarkerFile));

    public void Initialize()
    {
        Directory.CreateDirectory(ChunksFolder);
        Directory.CreateDirectory(SnapshotsFolder);
        string marker = Path.Combine(Root, MarkerFile);
        if (!File.Exists(marker))
        {
            File.WriteAllText(marker, "MegaCopier: copias de seguridad sin duplicados. No cambies nada a mano; para recuperar archivos usa MegaCopier > Restaurar.\r\n");
        }
    }

    public List<Snapshot> Snapshots()
    {
        if (!Directory.Exists(SnapshotsFolder))
        {
            return [];
        }

        var list = new List<Snapshot>();
        foreach (string file in Directory.EnumerateFiles(SnapshotsFolder, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllBytes(file), SnapshotJsonContext.Default.Snapshot) is { } snapshot)
                {
                    list.Add(snapshot);
                }
            }
            catch (JsonException)
            {
                // Una instantánea a medias (corte de luz al guardarla): no cuenta.
            }
        }

        return [.. list.OrderBy(s => s.CreatedUtc)];
    }

    public Snapshot? Find(string id) =>
        id is "" or "latest" ? Snapshots().LastOrDefault() : Snapshots().FirstOrDefault(s => s.Id == id);

    private string ChunkPath(string hash) => Path.Combine(ChunksFolder, hash[..2], hash);

    public bool HasChunk(string hash) => File.Exists(ChunkPath(hash));

    /// <summary>Guarda un trozo si no estaba ya; devuelve su hash y si era nuevo.</summary>
    public (string Hash, bool Stored) Store(ReadOnlySpan<byte> data)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(data));
        string path = ChunkPath(hash);
        if (File.Exists(path))
        {
            return (hash, false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string partial = path + FileTransfer.PartialSuffix;
        using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            using (var compressor = new DeflateStream(file, CompressionLevel.Fastest, leaveOpen: true))
            {
                compressor.Write(data);
            }

            file.Flush(flushToDisk: true);
        }

        File.Move(partial, path, overwrite: true);
        return (hash, true);
    }

    /// <summary>Lee un trozo comprobando que su contenido corresponde a su nombre.</summary>
    public byte[] Load(string hash)
    {
        using var file = new FileStream(ChunkPath(hash), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var decompressor = new DeflateStream(file, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        byte[] data = output.ToArray();
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), hash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(Texts.F("E_BackupChunkDamaged", hash[..12]));
        }

        return data;
    }

    public void Save(Snapshot snapshot)
    {
        Directory.CreateDirectory(SnapshotsFolder);
        string path = Path.Combine(SnapshotsFolder, snapshot.Id + ".json");
        string partial = path + FileTransfer.PartialSuffix;
        File.WriteAllBytes(partial, JsonSerializer.SerializeToUtf8Bytes(snapshot, SnapshotJsonContext.Default.Snapshot));
        File.Move(partial, path, overwrite: true);
    }

    /// <summary>Un identificador legible y ordenable: la fecha y hora local (con sufijo si hay dos en el mismo segundo).</summary>
    public string NewId(DateTime now)
    {
        string id = now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        for (int n = 2; File.Exists(Path.Combine(SnapshotsFolder, id + ".json")); n++)
        {
            id = now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture) + $" ({n})";
        }

        return id;
    }

    /// <summary>
    /// Quita instantáneas con la regla de días, semanas y meses (ver <see cref="VersionPruner"/>) y borra los trozos que
    /// ya no usa ninguna. Devuelve (instantáneas quitadas, trozos borrados).
    /// </summary>
    public (int Snapshots, int Chunks) Prune(int daily, int weekly, int monthly)
    {
        var snapshots = Snapshots();
        int removed = 0;
        if (daily + weekly + monthly > 0 && snapshots.Count > 1)
        {
            var newest = snapshots.OrderByDescending(s => s.CreatedUtc).ToList();
            var keep = new HashSet<string> { newest[0].Id };
            KeepPeriods(newest, s => s.CreatedUtc.ToLocalTime().Date, daily, keep);
            KeepPeriods(newest, s => (ISOWeek.GetYear(s.CreatedUtc.ToLocalTime()), ISOWeek.GetWeekOfYear(s.CreatedUtc.ToLocalTime())), weekly, keep);
            KeepPeriods(newest, s => (s.CreatedUtc.ToLocalTime().Year, s.CreatedUtc.ToLocalTime().Month), monthly, keep);
            foreach (var snapshot in snapshots.Where(s => !keep.Contains(s.Id)))
            {
                File.Delete(Path.Combine(SnapshotsFolder, snapshot.Id + ".json"));
                removed++;
            }
        }

        // Trozos sin dueño (de instantáneas quitadas o de una copia que se cortó a medias).
        var used = Snapshots().SelectMany(s => s.Files).SelectMany(f => f.Chunks).ToHashSet(StringComparer.Ordinal);
        int chunks = 0;
        if (Directory.Exists(ChunksFolder))
        {
            foreach (string path in Directory.EnumerateFiles(ChunksFolder, "*", SearchOption.AllDirectories))
            {
                if (!used.Contains(Path.GetFileName(path)))
                {
                    File.Delete(path);
                    chunks++;
                }
            }
        }

        return (removed, chunks);
    }

    private static void KeepPeriods<TKey>(List<Snapshot> newest, Func<Snapshot, TKey> period, int count, HashSet<string> keep)
        where TKey : notnull
    {
        var seen = new HashSet<TKey>();
        foreach (var snapshot in newest)
        {
            if (seen.Count >= count)
            {
                return;
            }

            if (seen.Add(period(snapshot)))
            {
                keep.Add(snapshot.Id);
            }
        }
    }
}
