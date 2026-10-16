using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>La mejor configuración medida para un disco.</summary>
public sealed class DeviceProfile
{
    public int BlockSizeMB { get; set; }

    public int QueueDepth { get; set; }

    public double MBps { get; set; }

    public DateTime MeasuredUtc { get; set; }
}

[JsonSerializable(typeof(Dictionary<string, DeviceProfile>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class DeviceProfileJsonContext : JsonSerializerContext;

/// <summary>
/// Autoajuste por disco: copia un archivo de prueba en el disco con varios tamaños de bloque y lecturas/escrituras
/// simultáneas, sin caché (velocidad real), se queda con la combinación más rápida y la recuerda por disco físico. Las
/// copias hacia ese disco la usan solas. Un disco mecánico, un SSD SATA y uno NVMe quieren cosas distintas.
/// </summary>
public static class DeviceTuner
{
    private static readonly int[] Blocks = [1, 4, 16];
    private static readonly int[] Depths = [1, 2, 4, 8];
    private static readonly Lock Gate = new();

    public static string ProfilesPath => DataFolder.Combine("devices.json");

    /// <summary>Clave del disco físico de una ruta (la misma que usa la cola).</summary>
    public static string DeviceOf(string path) => VolumeInfo.DeviceKey(path);

    public static Dictionary<string, DeviceProfile> Load()
    {
        lock (Gate)
        {
            try
            {
                return File.Exists(ProfilesPath)
                    ? JsonSerializer.Deserialize(File.ReadAllText(ProfilesPath), DeviceProfileJsonContext.Default.DictionaryStringDeviceProfile) ?? []
                    : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    /// <summary>El perfil medido del disco de esa ruta, o null.</summary>
    public static DeviceProfile? ProfileFor(string path) => Load().GetValueOrDefault(DeviceOf(path));

    /// <summary>
    /// Mide el disco de <paramref name="folder"/> con un archivo de prueba de <paramref name="testMB"/> MB (se borra al
    /// terminar) y guarda la mejor combinación. Devuelve el perfil, o null si no se pudo escribir allí.
    /// </summary>
    public static DeviceProfile? Measure(string folder, int testMB = 256, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        string work = Path.Combine(folder, ".megacopier-prueba-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(work);
            File.SetAttributes(work, FileAttributes.Directory | FileAttributes.Hidden);
            string source = Path.Combine(work, "origen.bin");
            WriteTestFile(source, testMB);

            DeviceProfile? best = null;
            foreach (int block in Blocks)
            {
                foreach (int depth in Depths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string destination = Path.Combine(work, $"d{block}-{depth}");
                    var options = new CopyOptions
                    {
                        BlockSize = block * CopyOptions.MiB,
                        ChunkSize = Math.Max(64L, block) * CopyOptions.MiB,
                        ReadQueueDepth = depth,
                        WriteQueueDepth = depth,
                        UseUnbufferedIo = true,
                        BlockClone = false,
                        CheckFreeSpace = false,
                        HashAlgorithm = FileHashAlgorithm.None,
                        ProgressInterval = TimeSpan.FromSeconds(10),
                    };
                    var watch = Stopwatch.StartNew();
                    new CopyEngine(options).Run(new CopyJobRequest([source], destination), cancellationToken);
                    double mbps = testMB / watch.Elapsed.TotalSeconds;
                    progress?.Invoke($"{block} MB × {depth}: {mbps:N0} MB/s");
                    Directory.Delete(destination, recursive: true);

                    // Un 3 % más rápido como mínimo para preferir una combinación más exigente.
                    if (best is null || mbps > best.MBps * 1.03)
                    {
                        best = new DeviceProfile { BlockSizeMB = block, QueueDepth = depth, MBps = Math.Round(mbps), MeasuredUtc = DateTime.UtcNow };
                    }
                }
            }

            if (best is not null)
            {
                Save(DeviceOf(folder), best);
            }

            return best;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CopyPlanException)
        {
            progress?.Invoke(ex.Message);
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(work))
                {
                    Directory.Delete(work, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Save(string device, DeviceProfile profile)
    {
        lock (Gate)
        {
            var all = Load();
            all[device] = profile;
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(all, DeviceProfileJsonContext.Default.DictionaryStringDeviceProfile));
        }
    }

    /// <summary>Datos aleatorios (no comprimibles), escritos sin caché y descartados de la caché para medir de verdad.</summary>
    private static void WriteTestFile(string path, int megabytes)
    {
        byte[] block = new byte[CopyOptions.MiB];
        new Random(42).NextBytes(block);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
        for (int i = 0; i < megabytes; i++)
        {
            block[0] = (byte)i;
            stream.Write(block);
        }
    }
}
