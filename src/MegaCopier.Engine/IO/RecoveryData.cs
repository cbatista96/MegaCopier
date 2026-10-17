using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace MegaCopier.Engine.IO;

/// <summary>Qué pasó al revisar un archivo con sus datos de recuperación.</summary>
public enum RepairOutcome
{
    /// <summary>Estaba bien.</summary>
    Intact,

    /// <summary>Tenía bloques dañados y se reconstruyeron.</summary>
    Repaired,

    /// <summary>Hay más bloques dañados de los que se pueden reconstruir.</summary>
    Unrepairable,

    /// <summary>El archivo se modificó después (otro tamaño o fecha): los datos de recuperación ya no le sirven.</summary>
    Changed,
}

/// <summary>
/// Datos de recuperación (como PAR2): junto a la copia se guarda, aparte, un poco de paridad Reed-Solomon. Si con el
/// tiempo el disco o el USB estropea bloques del archivo, se reconstruyen sin necesitar el original. El archivo se trata
/// en tramos de 200 bloques de 64 KB (12,5 MB): en cada tramo se pueden perder tantos bloques como paridad haya (por
/// ejemplo 20 bloques seguidos, 1,25 MB, con el 10 %). Se guardan en &lt;carpeta&gt;\.megacopier-recuperacion.
/// </summary>
public static class RecoveryData
{
    public const string Folder = ".megacopier-recuperacion";
    public const string Extension = ".mcrec";
    private const int BlockSize = 64 * 1024;
    private const int StripeBlocks = 200;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MCREC1");

    /// <summary>Dónde van los datos de recuperación de un archivo que está dentro de <paramref name="root"/>.</summary>
    public static string PathFor(string root, string file) =>
        Path.Combine(root, Folder, Path.GetRelativePath(root, file) + Extension);

    /// <summary>La carpeta (esta o una superior) que tiene datos de recuperación.</summary>
    public static string? FindRoot(string path)
    {
        for (string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (Directory.Exists(Path.Combine(folder, Folder)))
            {
                return folder;
            }
        }

        return null;
    }

    private static int ParityFor(int blocks, int percent) => Math.Max(1, (int)Math.Ceiling(blocks * percent / 100.0));

    /// <summary>Crea los datos de recuperación de un archivo (el <paramref name="percent"/> % de su tamaño, más o menos).</summary>
    public static void Create(string file, string recoveryPath, int percent, Action<long>? onRead = null, CancellationToken cancellationToken = default)
    {
        percent = Math.Clamp(percent, 1, 25);
        Directory.CreateDirectory(Path.GetDirectoryName(recoveryPath)!);
        string partial = recoveryPath + FileTransfer.PartialSuffix;
        var info = new FileInfo(file);
        using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
        using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            long length = input.Length;
            Span<byte> header = stackalloc byte[Magic.Length + 8 + 8 + 4];
            Magic.CopyTo(header);
            BinaryPrimitives.WriteInt64LittleEndian(header[6..], length);
            BinaryPrimitives.WriteInt64LittleEndian(header[14..], info.LastWriteTimeUtc.Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(header[22..], percent);
            output.Write(header);

            long blocks = Math.Max(1, (length + BlockSize - 1) / BlockSize);
            var data = new List<byte[]>();
            for (long first = 0; first < blocks; first += StripeBlocks)
            {
                int k = (int)Math.Min(StripeBlocks, blocks - first);
                int m = ParityFor(k, percent);
                while (data.Count < k)
                {
                    data.Add(new byte[BlockSize]);
                }

                var hashes = new byte[(k + m) * 8];
                for (int i = 0; i < k; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = input.ReadAtLeast(data[i], BlockSize, throwOnEndOfStream: false);
                    Array.Clear(data[i], read, BlockSize - read);
                    onRead?.Invoke(read);
                    BinaryPrimitives.WriteUInt64LittleEndian(hashes.AsSpan(i * 8), XxHash64.HashToUInt64(data[i]));
                }

                var parity = Enumerable.Range(0, m).Select(_ => new byte[BlockSize]).ToList();
                new ReedSolomon(k, m).Encode(data.Take(k).ToList(), parity, BlockSize);
                for (int j = 0; j < m; j++)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(hashes.AsSpan((k + j) * 8), XxHash64.HashToUInt64(parity[j]));
                }

                output.Write(hashes);
                foreach (byte[] block in parity)
                {
                    output.Write(block);
                }
            }

            output.Flush(flushToDisk: true);
        }

        File.Move(partial, recoveryPath, overwrite: true);
    }

    /// <summary>
    /// Revisa el archivo bloque a bloque y reconstruye lo dañado (también los bloques que no se pueden leer). Devuelve
    /// cuántos bloques se arreglaron.
    /// </summary>
    public static (RepairOutcome Outcome, int Blocks) Repair(string file, string recoveryPath, Action<long>? onRead = null, CancellationToken cancellationToken = default)
    {
        using var recovery = new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        Span<byte> header = stackalloc byte[Magic.Length + 8 + 8 + 4];
        recovery.ReadExactly(header);
        if (!header[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException(Texts.Get("E_RecoveryDamaged"));
        }

        long length = BinaryPrimitives.ReadInt64LittleEndian(header[6..]);
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(header[14..]);
        int percent = BinaryPrimitives.ReadInt32LittleEndian(header[22..]);
        var info = new FileInfo(file);
        if (!info.Exists || info.Length != length || Math.Abs((info.LastWriteTimeUtc - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds) > 2)
        {
            return (RepairOutcome.Changed, 0);
        }

        DateTime lastWrite = info.LastWriteTimeUtc;
        int repaired = 0;
        bool unrepairable = false;
        long blocks = Math.Max(1, (length + BlockSize - 1) / BlockSize);
        using (var target = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.WriteThrough))
        {
            for (long first = 0; first < blocks; first += StripeBlocks)
            {
                int k = (int)Math.Min(StripeBlocks, blocks - first);
                int m = ParityFor(k, percent);
                byte[] hashes = new byte[(k + m) * 8];
                recovery.ReadExactly(hashes);
                var data = new byte[]?[k];
                for (int i = 0; i < k; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long offset = (first + i) * BlockSize;
                    byte[] block = new byte[BlockSize];
                    try
                    {
                        target.Position = offset;
                        int read = target.ReadAtLeast(block, (int)Math.Min(BlockSize, length - offset), throwOnEndOfStream: false);
                        onRead?.Invoke(read);
                        data[i] = XxHash64.HashToUInt64(block) == BinaryPrimitives.ReadUInt64LittleEndian(hashes.AsSpan(i * 8)) ? block : null;
                    }
                    catch (IOException)
                    {
                        data[i] = null; // sector ilegible: se reconstruye
                    }
                }

                var parity = new byte[]?[m];
                for (int j = 0; j < m; j++)
                {
                    byte[] block = new byte[BlockSize];
                    recovery.ReadExactly(block);
                    parity[j] = XxHash64.HashToUInt64(block) == BinaryPrimitives.ReadUInt64LittleEndian(hashes.AsSpan((k + j) * 8)) ? block : null;
                }

                var lost = Enumerable.Range(0, k).Where(i => data[i] is null).ToList();
                if (lost.Count == 0)
                {
                    continue;
                }

                if (!new ReedSolomon(k, m).Reconstruct(data, parity, BlockSize))
                {
                    unrepairable = true;
                    continue;
                }

                foreach (int i in lost)
                {
                    long offset = (first + i) * BlockSize;
                    int count = (int)Math.Min(BlockSize, length - offset);
                    if (XxHash64.HashToUInt64(data[i]!) != BinaryPrimitives.ReadUInt64LittleEndian(hashes.AsSpan(i * 8)))
                    {
                        unrepairable = true; // no debería pasar: la paridad también estaba mal sin que se notara
                        continue;
                    }

                    target.Position = offset;
                    target.Write(data[i]!, 0, count);
                    repaired++;
                }
            }

            target.Flush(flushToDisk: true);
        }

        File.SetLastWriteTimeUtc(file, lastWrite); // reparar no es modificar
        return (unrepairable ? RepairOutcome.Unrepairable : repaired > 0 ? RepairOutcome.Repaired : RepairOutcome.Intact, repaired);
    }
}
