using System.Collections.Concurrent;
using MegaCopier.Engine.IO;

namespace MegaCopier.Engine;

internal sealed partial class FileTransfer
{
    /// <summary>Desde este tamaño compensa leer un archivo de varios sitios a la vez.</summary>
    public const long MultiSourceThreshold = 64L * 1024 * 1024;

    private const int SegmentSize = 8 * 1024 * 1024;

    /// <summary>Otras copias del mismo archivo (otro NAS, otro equipo...), si las hay: las da el motor.</summary>
    public Func<string, IReadOnlyList<string>>? Alternates { get; set; }

    /// <summary>
    /// Varios orígenes a la vez (como BitTorrent en la red local): el archivo se reparte en trozos de 8 MB y cada origen
    /// (el de siempre y las copias alternativas con el mismo tamaño y fecha) lee los suyos, que se escriben en su sitio.
    /// Si una copia alternativa falla, sus trozos los hacen las demás. Con "verificar", la copia se compara con el hash
    /// guardado en el original (o, si no tiene, con el original releído). Null = no aplica (se copia como siempre).
    /// </summary>
    private FileTransferResult? CopyMultiSource(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (Alternates?.Invoke(sourcePath) is not { Count: > 0 } candidates)
        {
            return null;
        }

        var info = new FileInfo(sourcePath);
        var usable = candidates.Where(path =>
        {
            var other = new FileInfo(path);
            return other.Exists && other.Length == info.Length && (other.LastWriteTimeUtc - info.LastWriteTimeUtc).Duration() < TimeSpan.FromSeconds(2)
                && SameEnds(sourcePath, path, info.Length);
        }).ToList();
        string partialPath = destinationPath + PartialSuffix;
        if (usable.Count == 0 || File.Exists(partialPath))
        {
            return null; // nadie más lo tiene igual, o hay una copia a medias que se reanuda por el camino normal
        }

        long length = info.Length;
        long segments = (length + SegmentSize - 1) / SegmentSize;
        long next = -1;
        var retry = new ConcurrentQueue<long>();
        Exception? primaryFailure = null;
        int alive = usable.Count + 1;
        var result = new FileTransferResult { BytesTransferred = length, Sources = usable.Count + 1 };
        _current = result;
        try
        {
            using (var destination = File.OpenHandle(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None, length))
            {
                void Worker(string path, bool primary)
                {
                    long segment = -1;
                    try
                    {
                        using var source = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
                        byte[] buffer = new byte[SegmentSize];
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            _controller.WaitIfPaused(cancellationToken);
                            if (!retry.TryDequeue(out segment) && (segment = Interlocked.Increment(ref next)) >= segments)
                            {
                                segment = -1;
                                return;
                            }

                            long offset = segment * SegmentSize;
                            int count = (int)Math.Min(SegmentSize, length - offset);
                            int filled = 0;
                            while (filled < count)
                            {
                                int read = RandomAccess.Read(source, buffer.AsSpan(filled, count - filled), offset + filled);
                                if (read == 0)
                                {
                                    throw new IOException(path);
                                }

                                filled += read;
                            }

                            if (_options.MaxBytesPerSecond > 0)
                            {
                                _throttle?.WaitAsync(count, cancellationToken).GetAwaiter().GetResult();
                            }

                            RandomAccess.Write(destination, buffer.AsSpan(0, count), offset);
                            _progress.AddTransferred(count);
                            segment = -1;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (segment >= 0)
                        {
                            retry.Enqueue(segment); // lo hace otro origen
                        }

                        if (primary)
                        {
                            primaryFailure = ex;
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref alive);
                    }
                }

                var workers = usable.Select(path => Task.Run(() => Worker(path, primary: false), cancellationToken)).Prepend(Task.Run(() => Worker(sourcePath, primary: true), cancellationToken)).ToArray();
                Task.WaitAll(workers, cancellationToken);
                if (!retry.IsEmpty)
                {
                    // Todos los que quedaban fallaron con trozos pendientes: que lo intente el camino normal.
                    throw primaryFailure ?? new IOException(sourcePath);
                }

                RandomAccess.FlushToDisk(destination);
            }

            if (_options.Verify || CopyEngine.ReadHashStream(sourcePath) is not null)
            {
                _progress.SetPhase(CopyPhase.Verifying);
                var stored = CopyEngine.ReadHashStream(sourcePath);
                var algorithm = stored?.Algorithm ?? (_options.HashAlgorithm == FileHashAlgorithm.None ? FileHashAlgorithm.XxHash3 : _options.HashAlgorithm);
                result.SourceHash = stored is { } known && (known.LastWriteUtc is null || (known.LastWriteUtc.Value - info.LastWriteTimeUtc).Duration() < TimeSpan.FromSeconds(2))
                    ? known.Hash
                    : HashFile(sourcePath, algorithm, cancellationToken);
                result.DestinationHash = HashFile(partialPath, algorithm, cancellationToken);
                _progress.SetPhase(CopyPhase.Copying);
                if (!string.Equals(result.SourceHash, result.DestinationHash, StringComparison.OrdinalIgnoreCase))
                {
                    IoHelpers.DeleteIfExists(partialPath);
                    return null; // alguna copia alternativa no era igual: se copia otra vez solo del original
                }
            }

            File.SetLastWriteTimeUtc(partialPath, info.LastWriteTimeUtc);
            if (File.Exists(destinationPath))
            {
                File.SetAttributes(destinationPath, FileAttributes.Normal);
            }

            File.Move(partialPath, destinationPath, overwrite: true);
            File.SetAttributes(destinationPath, info.Attributes & PreservedAttributes);
            return result;
        }
        catch
        {
            IoHelpers.DeleteIfExists(partialPath);
            throw;
        }
    }

    /// <summary>Comprobación rápida de que una copia alternativa es la misma: el principio, el medio y el final coinciden.</summary>
    private static bool SameEnds(string a, string b, long length)
    {
        try
        {
            using var first = File.OpenHandle(a, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var second = File.OpenHandle(b, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] x = new byte[64 * 1024], y = new byte[64 * 1024];
            foreach (long offset in new[] { 0, Math.Max(0, (length / 2) - x.Length), Math.Max(0, length - x.Length) })
            {
                int read = RandomAccess.Read(first, x, offset);
                if (RandomAccess.Read(second, y, offset) != read || !x.AsSpan(0, read).SequenceEqual(y.AsSpan(0, read)))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string HashFile(string path, FileHashAlgorithm algorithm, CancellationToken cancellationToken)
    {
        var hasher = FileHasher.Create(algorithm)!;
        byte[] buffer = new byte[1 << 20];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasher.Append(buffer.AsSpan(0, read));
        }

        return hasher.Finish();
    }
}
