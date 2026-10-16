using System.Buffers;
using System.IO.Hashing;
using MegaCopier.Engine.IO;
using MegaCopier.Engine.Journal;
using MegaCopier.Engine.Native;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine;

internal sealed class FileTransferResult
{
    public long BytesTransferred { get; set; }

    public long BytesResumed { get; set; }

    public int ChunksRejectedOnResume { get; set; }

    public int ChunksRepaired { get; set; }

    /// <summary>Hash completo del origen con el algoritmo elegido (null si no se calcula).</summary>
    public string? SourceHash { get; set; }

    /// <summary>Hash completo del destino releído sin caché (solo al verificar).</summary>
    public string? DestinationHash { get; set; }

    /// <summary>Avisos que no impiden la copia (streams o permisos que no se pudieron copiar).</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Se clonaron los bloques (ReFS/Dev Drive) en lugar de copiar los datos.</summary>
    public bool Cloned { get; set; }

    /// <summary>Lo copió Windows (CopyFile2): en el servidor o con compresión SMB.</summary>
    public bool SystemCopied { get; set; }
}

/// <summary>
/// Copia un único archivo de forma que sobreviva a un apagón:
///
///  1. Escribe en "destino.mcpart", nunca en el nombre final.
///  2. Copia por bloques. Al cerrar cada bloque: vacía los datos al disco y SOLO DESPUÉS lo anota en "destino.mcjournal".
///  3. Al reanudar manda el journal, no el tamaño del archivo; los últimos bloques se releen y se comprueba su hash.
///  4. Al terminar (y verificar, si se pidió) renombra a su nombre final y borra el journal.
///
/// Rendimiento: E/S asíncrona superpuesta con varias lecturas y escrituras en vuelo a la vez
/// (<see cref="CopyOptions.ReadQueueDepth"/> / <see cref="CopyOptions.WriteQueueDepth"/>); un NVMe necesita
/// varias peticiones simultáneas para rendir. El hash se calcula en orden mientras las escrituras siguen en curso.
/// </summary>
internal sealed class FileTransfer
{
    public const string PartialSuffix = ".mcpart";
    public const string JournalSuffix = ".mcjournal";

    private const FileAttributes PreservedAttributes =
        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed;

    private readonly CopyOptions _options;
    private readonly BufferPool _pool;
    private readonly CopyController _controller;
    private readonly ProgressTracker _progress;
    private readonly IFaultInjector? _faults;
    private readonly Action<string, long>? _chunkCommitted;
    private readonly Throttle? _throttle;

    public FileTransfer(
        CopyOptions options,
        BufferPool pool,
        CopyController controller,
        ProgressTracker progress,
        IFaultInjector? faults,
        Action<string, long>? chunkCommitted)
    {
        _options = options;
        _pool = pool;
        _controller = controller;
        _progress = progress;
        _faults = faults;
        _chunkCommitted = chunkCommitted;
        _throttle = new Throttle(() => options.MaxBytesPerSecond);
    }

    /// <summary>Búferes que necesita una transferencia: uno por cada lectura y escritura en vuelo.</summary>
    public static int RequiredBuffers(CopyOptions options) => options.ReadQueueDepth + options.WriteQueueDepth;

    /// <summary>Hasta este tamaño, la copia va por el camino rápido síncrono (<see cref="CopySmall"/>).</summary>
    public const long SmallFileLimit = 1024 * 1024;

    public FileTransferResult Copy(string sourcePath, string destinationPath, CancellationToken cancellationToken, long lengthHint = -1)
    {
        if (lengthHint is >= 0 and <= SmallFileLimit && _faults is null && _options.MaxBytesPerSecond <= 0
            && CopySmall(sourcePath, destinationPath, cancellationToken) is { } small)
        {
            return small;
        }

        return CopyAsync(sourcePath, destinationPath, cancellationToken).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Archivo pequeño: se lee entero, se escribe de una vez y se verifica releyendo del disco (sin caché). En un
    /// archivo de pocos KB, la E/S asíncrona por bloques cuesta más que los datos; así es varias veces más rápido.
    /// Null si el archivo ya no es pequeño (creció desde que se planificó): entonces se copia por el camino normal.
    /// </summary>
    private FileTransferResult? CopySmall(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        string partialPath = destinationPath + PartialSuffix;
        using var source = File.OpenHandle(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        var info = Win32.GetSourceInfo(source);
        long length = info.Identity.Length;
        if (length > SmallFileLimit)
        {
            return null;
        }

        _controller.WaitIfPaused(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] data = ArrayPool<byte>.Shared.Rent((int)Math.Max(1, length));
        try
        {
            var span = data.AsSpan(0, (int)length);
            int total = 0;
            while (total < length)
            {
                int read = RandomAccess.Read(source, span[total..], total);
                if (read == 0)
                {
                    throw SourceChanged();
                }

                total += read;
            }

            var result = new FileTransferResult { BytesTransferred = length };
            if (FileHasher.Create(_options.HashAlgorithm) is { } hasher)
            {
                hasher.Append(span);
                result.SourceHash = hasher.Finish();
            }

            using (var destination = SmallFile.Create(partialPath, _options.FlushSmallFiles))
            {
                RandomAccess.Write(destination, span, 0);
                _progress.AddTransferred(length);

                if (_options.Verify && result.SourceHash is { } expected)
                {
                    _progress.SetPhase(CopyPhase.Verifying);
                    result.DestinationHash = HashFromDisk(partialPath, length);
                    if (result.DestinationHash != expected)
                    {
                        // Se reescribe una vez (como la reparación por bloques de los archivos grandes).
                        RandomAccess.Write(destination, span, 0);
                        result.DestinationHash = HashFromDisk(partialPath, length);
                        result.ChunksRepaired = result.DestinationHash == expected ? 1 : 0;
                    }

                    _progress.SetPhase(CopyPhase.Copying);
                }

                // Streams alternativos antes de las fechas (escribirlos cambia la fecha de modificación).
                if (_options.CopyStreams)
                {
                    try
                    {
                        FileMetadata.CopyAlternateStreams(sourcePath, partialPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        result.Warnings.Add($"No se copiaron los streams alternativos: {ex.Message}");
                    }
                }

                // Fechas, atributos y nombre final con el mismo identificador: sin volver a abrir el archivo.
                SmallFile.SetBasicInfo(
                    destination,
                    _options.PreserveTimestamps ? info.CreationUtc : null,
                    _options.PreserveTimestamps ? info.LastWriteUtc : null,
                    _options.PreserveAttributes ? info.Attributes & PreservedAttributes : null);
                SmallFile.Rename(destination, destinationPath);
            }

            if (_options.CopySecurity != SecurityParts.None)
            {
                try
                {
                    FileMetadata.CopySecurity(sourcePath, destinationPath, _options.CopySecurity);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Warnings.Add(ex.Message);
                }
            }

            return result;
        }
        catch
        {
            IoHelpers.DeleteIfExists(partialPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(data);
        }
    }

    /// <summary>Hash de lo escrito leyéndolo del disco, sin la caché de Windows (si el disco lo permite).</summary>
    private string? HashFromDisk(string path, long length)
    {
        var hasher = FileHasher.Create(_options.HashAlgorithm)!;
        long rounded = (length + 4095) / 4096 * 4096;
        if (_options.UseUnbufferedIo && IoHelpers.SupportsUnbuffered(path) && rounded <= _pool.BufferSize && _pool.TryRent(out var buffer))
        {
            try
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Win32.NoBuffering);
                int read = RandomAccess.Read(handle, buffer.Span[..(int)rounded], 0);
                hasher.Append(buffer.Span[..Math.Min(read, (int)length)]);
                return hasher.Finish();
            }
            finally
            {
                _pool.Return(buffer);
            }
        }

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            byte[] bytes = new byte[length];
            stream.ReadExactly(bytes);
            hasher.Append(bytes);
        }

        return hasher.Finish();
    }

    private async Task<FileTransferResult> CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        string partialPath = destinationPath + PartialSuffix;
        string journalPath = destinationPath + JournalSuffix;

        using var source = OpenRead(sourcePath, out bool sourceUnbuffered);
        var info = Win32.GetSourceInfo(source);
        long length = info.Identity.Length;
        bool resumable = length >= _options.ResumableThreshold;

        TransferJournal? journal = resumable ? TryOpenJournal(journalPath, partialPath, info.Identity) : null;

        // Un archivo pequeño no deja diario y su .mcpart se crea encima: no hace falta buscar restos (dos llamadas menos).
        if (journal is null && resumable)
        {
            IoHelpers.DeleteIfExists(journalPath);
            IoHelpers.DeleteIfExists(partialPath);
        }

        if (journal is null && _options.BlockClone && length > 0
            && await TryCloneAsync(source, sourceUnbuffered, info, sourcePath, partialPath, destinationPath, cancellationToken).ConfigureAwait(false) is { } cloned)
        {
            return cloned;
        }

        bool systemCopy = _options.ForceSystemCopy
            || (_options.ServerSideCopy && SystemCopy.SameServer(sourcePath, destinationPath))
            || (_options.SmbCompression && SystemCopy.OnNetwork(sourcePath, destinationPath));
        if (journal is null && systemCopy
            && await TrySystemCopyAsync(source, sourceUnbuffered, info, sourcePath, partialPath, destinationPath, cancellationToken).ConfigureAwait(false) is { } copied)
        {
            return copied;
        }

        var result = new FileTransferResult();
        SafeFileHandle? destination = null;
        try
        {
            bool resuming = journal is not null;
            destination = OpenWrite(partialPath, resuming ? FileMode.Open : FileMode.Create, out bool destinationUnbuffered);
            var io = new IoContext(source, sourceUnbuffered, destination, destinationUnbuffered);

            if (!resuming)
            {
                // Reserva el espacio de una vez: menos fragmentación y error inmediato si no cabe.
                if (length > 0)
                {
                    RandomAccess.SetLength(destination, length);
                }

                if (resumable)
                {
                    journal = TransferJournal.Create(journalPath, _options.ChunkSize, info.Identity);
                }
            }

            long chunkSize = journal?.ChunkSize ?? _options.ChunkSize;
            var chunks = new List<ChunkRecord>(journal?.LoadedChunks ?? []);

            if (resuming && chunks.Count > 0)
            {
                _progress.SetPhase(CopyPhase.Validating);
                int valid = await ValidateCommittedAsync(io, chunks, chunkSize, cancellationToken).ConfigureAwait(false);
                if (valid < chunks.Count)
                {
                    result.ChunksRejectedOnResume = chunks.Count - valid;
                    chunks.RemoveRange(valid, chunks.Count - valid);
                    journal!.TruncateTo(valid);
                }

                _progress.SetPhase(CopyPhase.Copying);
            }

            long start = chunks.Sum(c => (long)c.Length);
            result.BytesResumed = start;
            _progress.SetFileResumed(start);

            // Hash completo del archivo: al reanudar, la parte ya copiada se hashea leyendo el origen.
            var fileHasher = FileHasher.Create(_options.HashAlgorithm);
            if (fileHasher is not null && start > 0)
            {
                await ReadSequentialAsync(io.Source, io.SourceUnbuffered, 0, start, (data, _) => fileHasher.Append(data), cancellationToken).ConfigureAwait(false);
            }

            var writer = new ChunkWriter(this, io, destinationPath, chunkSize, length, chunks, journal, fileHasher);
            await CopyRangeAsync(io, start, length, writer, cancellationToken).ConfigureAwait(false);
            result.BytesTransferred = length - start;
            result.SourceHash = fileHasher?.Finish();

            if (destinationUnbuffered || RandomAccess.GetLength(destination) != length)
            {
                RandomAccess.SetLength(destination, length);
            }

            if (resumable || _options.FlushSmallFiles)
            {
                Win32.Flush(destination);
            }

            if (_options.Verify)
            {
                var destinationHasher = FileHasher.Create(_options.HashAlgorithm);
                result.ChunksRepaired = await VerifyAndRepairAsync(io, chunks, chunkSize, length, destinationPath, destinationHasher, cancellationToken).ConfigureAwait(false);

                // Tras reparar, cada bloque coincide con el origen: el hash del destino es el del origen.
                result.DestinationHash = result.ChunksRepaired > 0 ? result.SourceHash : destinationHasher?.Finish();
            }
        }
        catch
        {
            // Se conservan .mcpart y .mcjournal: la próxima ejecución continúa desde el último bloque confirmado.
            destination?.Dispose();
            journal?.Dispose();
            throw;
        }

        destination.Dispose();
        Finalize(sourcePath, partialPath, destinationPath, info, result);
        journal?.Delete();
        return result;
    }

    /// <summary>
    /// ReFS y Dev Drive: el destino comparte los bloques del origen (instantáneo). Null si no se puede (otro volumen, otro
    /// sistema de archivos o el clonado falla): entonces se copia de la forma normal.
    /// </summary>
    /// <summary>Volúmenes (por número de serie) que saben clonar bloques: se pregunta una vez, no con cada archivo.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, bool> CloneVolumes = new();

    private async Task<FileTransferResult?> TryCloneAsync(
        SafeFileHandle source, bool sourceUnbuffered, SourceInfo info, string sourcePath, string partialPath, string destinationPath, CancellationToken cancellationToken)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(destinationPath))!;
        try
        {
            if (!CloneVolumes.GetOrAdd(info.Identity.VolumeSerial, _ => BlockClone.SupportsCloning(source)) || !BlockClone.CanClone(source, folder) || Win32.VolumeRoot(folder) is not { } volume)
            {
                return null;
            }

            using var destination = File.OpenHandle(partialPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            BlockClone.Clone(source, destination, volume, info.Identity.Length, (info.Attributes & FileAttributes.SparseFile) != 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IoHelpers.DeleteIfExists(partialPath);
            return null;
        }

        long length = info.Identity.Length;
        var result = new FileTransferResult { Cloned = true, BytesTransferred = length };
        _progress.AddTransferred(length);
        return await FinishShortcutAsync(source, sourceUnbuffered, info, sourcePath, partialPath, destinationPath, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copia del sistema (CopyFile2): en el mismo servidor la hace el propio servidor; con compresión SMB, comprimida.
    /// Null si falla por algo que la copia normal puede resolver.
    /// </summary>
    private async Task<FileTransferResult?> TrySystemCopyAsync(
        SafeFileHandle source, bool sourceUnbuffered, SourceInfo info, string sourcePath, string partialPath, string destinationPath, CancellationToken cancellationToken)
    {
        var result = new FileTransferResult { SystemCopied = true, BytesTransferred = info.Identity.Length };
        try
        {
            IoHelpers.DeleteIfExists(partialPath);
            bool network = SystemCopy.OnNetwork(sourcePath, destinationPath);
            SystemCopy.Copy(sourcePath, partialPath, _options.SmbCompression && network, _options.UseUnbufferedIo && !network, _progress.AddTransferred, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && !cancellationToken.IsCancellationRequested)
        {
            IoHelpers.DeleteIfExists(partialPath);
            return null;
        }
        catch (OperationCanceledException)
        {
            IoHelpers.DeleteIfExists(partialPath);
            throw;
        }

        return await FinishShortcutAsync(source, sourceUnbuffered, info, sourcePath, partialPath, destinationPath, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tras clonar o copiar con el sistema: sumas (archivo de sumas, verificación) y metadatos, como siempre.</summary>
    private async Task<FileTransferResult> FinishShortcutAsync(
        SafeFileHandle source, bool sourceUnbuffered, SourceInfo info, string sourcePath, string partialPath, string destinationPath,
        FileTransferResult result, CancellationToken cancellationToken)
    {
        long length = info.Identity.Length;
        if (FileHasher.Create(_options.HashAlgorithm) is { } sourceHasher)
        {
            await ReadSequentialAsync(source, sourceUnbuffered, 0, length, (data, _) => sourceHasher.Append(data), cancellationToken).ConfigureAwait(false);
            result.SourceHash = sourceHasher.Finish();
            if (_options.Verify && FileHasher.Create(_options.HashAlgorithm) is { } destinationHasher)
            {
                _progress.SetPhase(CopyPhase.Verifying);
                using var copy = File.OpenHandle(partialPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await ReadSequentialAsync(copy, false, 0, length, (data, _) => destinationHasher.Append(data), cancellationToken).ConfigureAwait(false);
                result.DestinationHash = destinationHasher.Finish();
                _progress.SetPhase(CopyPhase.Copying);
            }
        }

        Finalize(sourcePath, partialPath, destinationPath, info, result);
        return result;
    }

    private TransferJournal? TryOpenJournal(string journalPath, string partialPath, SourceIdentity identity)
    {
        if (!File.Exists(partialPath))
        {
            return null;
        }

        TransferJournal? journal;
        try
        {
            journal = TransferJournal.TryOpen(journalPath);
        }
        catch (IOException)
        {
            return null;
        }

        if (journal is null)
        {
            return null;
        }

        // Si el origen cambió (tamaño, fecha o identidad) o el bloque no encaja, lo copiado no sirve.
        if (journal.Source != identity || journal.ChunkSize % _pool.BufferSize != 0 || journal.ChunkSize > int.MaxValue)
        {
            journal.Dispose();
            return null;
        }

        return journal;
    }

    /// <summary>Devuelve cuántos bloques confirmados son realmente válidos en el disco.</summary>
    private async Task<int> ValidateCommittedAsync(IoContext io, List<ChunkRecord> chunks, long chunkSize, CancellationToken cancellationToken)
    {
        long partialLength = RandomAccess.GetLength(io.Destination);
        for (int i = 0; i < chunks.Count; i++)
        {
            if ((i * chunkSize) + chunks[i].Length > partialLength)
            {
                return i;
            }
        }

        int from = Math.Max(0, chunks.Count - _options.ResumeVerifyChunks);
        var bad = await FindBadChunksAsync(io.Destination, io.DestinationUnbuffered, chunks, chunkSize, from, chunks.Count, cancellationToken).ConfigureAwait(false);
        return bad.Count > 0 ? bad[0] : chunks.Count;
    }

    /// <summary>
    /// Canalización de copia: mantiene hasta <see cref="CopyOptions.ReadQueueDepth"/> lecturas en vuelo y entrega los
    /// bloques en orden al <see cref="ChunkWriter"/>, que a su vez mantiene varias escrituras en vuelo.
    /// </summary>
    private async Task CopyRangeAsync(IoContext io, long start, long length, ChunkWriter writer, CancellationToken cancellationToken)
    {
        var pending = new Queue<PendingRead>();
        long next = start;
        try
        {
            while (true)
            {
                while (next < length && pending.Count < _options.ReadQueueDepth && _pool.TryRent(out var buffer))
                {
                    int want = (int)Math.Min(_pool.BufferSize, length - next);
                    pending.Enqueue(new PendingRead(
                        IoHelpers.ReadFullAsync(io.Source, io.SourceUnbuffered, buffer, want, next, cancellationToken),
                        buffer,
                        next,
                        want));
                    next += want;
                }

                if (!pending.TryDequeue(out var read))
                {
                    if (next < length)
                    {
                        throw new InvalidOperationException("La canalización se quedó sin búferes.");
                    }

                    break;
                }

                int count;
                try
                {
                    count = await read.Task.ConfigureAwait(false);
                    if (count != read.Want)
                    {
                        throw SourceChanged();
                    }
                }
                catch
                {
                    _pool.Return(read.Buffer);
                    throw;
                }

                // El escritor se queda con el búfer y lo devuelve al terminar su escritura.
                await writer.AcceptAsync(read.Buffer, read.Offset, count, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await DrainReadsAsync(pending).ConfigureAwait(false);
            await writer.AbandonAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Relee el destino en una sola pasada (con varias lecturas en vuelo) y compara cada bloque con el hash del origen.
    /// Los bloques dañados se vuelven a copiar desde el origen; si siguen sin coincidir, la copia falla.
    /// En discos locales la lectura es sin caché, así que se comprueba lo que de verdad hay en el disco.
    /// </summary>
    private async Task<int> VerifyAndRepairAsync(
        IoContext io,
        List<ChunkRecord> chunks,
        long chunkSize,
        long length,
        string destinationPath,
        FileHasher? destinationHasher,
        CancellationToken cancellationToken)
    {
        _faults?.Hit(FaultPoint.BeforeVerify, -1, io.Destination);
        _progress.SetPhase(CopyPhase.Verifying);

        var bad = await FindBadChunksAsync(io.Destination, io.DestinationUnbuffered, chunks, chunkSize, 0, chunks.Count, cancellationToken, destinationHasher).ConfigureAwait(false);
        if (bad.Count > 0)
        {
            foreach (int index in bad)
            {
                await RecopyChunkAsync(io, index * chunkSize, chunks[index], cancellationToken).ConfigureAwait(false);
            }

            RandomAccess.SetLength(io.Destination, length);
            Win32.Flush(io.Destination);

            foreach (int index in bad)
            {
                var still = await FindBadChunksAsync(io.Destination, io.DestinationUnbuffered, chunks, chunkSize, index, index + 1, cancellationToken).ConfigureAwait(false);
                if (still.Count > 0)
                {
                    throw new CopyVerificationException($"El bloque {index} de \"{destinationPath}\" no coincide con el origen ni siquiera tras repararlo.");
                }
            }
        }

        _progress.SetPhase(CopyPhase.Copying);
        return bad.Count;
    }

    /// <summary>Lee los bloques [first, end) en orden y devuelve los índices cuyo hash no coincide (o que no se pudieron leer completos).</summary>
    private async Task<List<int>> FindBadChunksAsync(
        SafeFileHandle handle,
        bool unbuffered,
        List<ChunkRecord> chunks,
        long chunkSize,
        int first,
        int end,
        CancellationToken cancellationToken,
        FileHasher? fileHasher = null)
    {
        var bad = new List<int>();
        if (first >= end)
        {
            return bad;
        }

        long offset = first * chunkSize;
        long length = ((end - 1) * chunkSize) + chunks[end - 1].Length - offset;

        var hasher = new XxHash128();
        int current = first;
        int filled = 0;

        await ReadSequentialAsync(handle, unbuffered, offset, length, (data, _) =>
        {
            fileHasher?.Append(data);
            while (!data.IsEmpty && current < end)
            {
                int take = Math.Min(data.Length, chunks[current].Length - filled);
                hasher.Append(data[..take]);
                data = data[take..];
                filled += take;

                if (filled == chunks[current].Length)
                {
                    if (hasher.GetCurrentHashAsUInt128() != chunks[current].Hash)
                    {
                        bad.Add(current);
                    }

                    hasher.Reset();
                    filled = 0;
                    current++;
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        // Lo que no se llegó a leer (archivo más corto de lo esperado) también es inválido.
        for (int i = current; i < end; i++)
        {
            bad.Add(i);
        }

        return bad;
    }

    private async Task RecopyChunkAsync(IoContext io, long offset, ChunkRecord record, CancellationToken cancellationToken)
    {
        var hasher = new XxHash128();
        long read = await ReadSequentialAsync(io.Source, io.SourceUnbuffered, offset, record.Length, (data, position) =>
        {
            hasher.Append(data);
            WriteSync(io, data, position);
        }, cancellationToken).ConfigureAwait(false);

        if (read != record.Length)
        {
            throw SourceChanged();
        }

        if (hasher.GetCurrentHashAsUInt128() != record.Hash)
        {
            throw new CopyVerificationException("El archivo de origen ya no coincide con lo que se copió al principio.");
        }

        Win32.Flush(io.Destination);
    }

    /// <summary>Escritura síncrona para las reparaciones (poco frecuentes): copia a un búfer alineado si hace falta.</summary>
    private void WriteSync(IoContext io, ReadOnlySpan<byte> data, long position)
    {
        if (!io.DestinationUnbuffered)
        {
            RandomAccess.Write(io.Destination, data, position);
            return;
        }

        var buffer = _pool.Rent();
        try
        {
            data.CopyTo(buffer.Span);
            int aligned = IoHelpers.AlignUp(data.Length);
            buffer.Span[data.Length..aligned].Clear();
            RandomAccess.Write(io.Destination, buffer.Span[..aligned], position);
        }
        finally
        {
            _pool.Return(buffer);
        }
    }

    /// <summary>
    /// Lee [offset, offset + length) en orden con varias lecturas en vuelo y entrega cada bloque a <paramref name="consume"/>.
    /// Devuelve los bytes leídos (menos de los pedidos si el archivo es más corto).
    /// </summary>
    private async Task<long> ReadSequentialAsync(
        SafeFileHandle handle,
        bool unbuffered,
        long offset,
        long length,
        ReadOnlySpanAction<byte, long> consume,
        CancellationToken cancellationToken)
    {
        var pending = new Queue<PendingRead>();
        long end = offset + length;
        long next = offset;
        long total = 0;

        // Deja un búfer libre para WriteSync durante las reparaciones.
        int depth = Math.Max(1, Math.Min(_options.ReadQueueDepth, _pool.Available - 1));
        try
        {
            while (true)
            {
                while (next < end && pending.Count < depth && _pool.TryRent(out var buffer))
                {
                    int want = (int)Math.Min(_pool.BufferSize, end - next);
                    pending.Enqueue(new PendingRead(IoHelpers.ReadFullAsync(handle, unbuffered, buffer, want, next, cancellationToken), buffer, next, want));
                    next += want;
                }

                if (!pending.TryDequeue(out var read))
                {
                    break;
                }

                try
                {
                    int count = await read.Task.ConfigureAwait(false);
                    await _controller.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                    consume(read.Buffer.Span[..count], read.Offset);
                    total += count;
                    if (count < read.Want)
                    {
                        break;
                    }
                }
                finally
                {
                    _pool.Return(read.Buffer);
                }
            }
        }
        finally
        {
            await DrainReadsAsync(pending).ConfigureAwait(false);
        }

        return total;
    }

    private async Task DrainReadsAsync(Queue<PendingRead> pending)
    {
        while (pending.TryDequeue(out var read))
        {
            try
            {
                await read.Task.ConfigureAwait(false);
            }
            catch
            {
                // Ya se está propagando otro error; aquí solo se recuperan los búferes.
            }

            _pool.Return(read.Buffer);
        }
    }

    private void Finalize(string sourcePath, string partialPath, string destinationPath, SourceInfo info, FileTransferResult result)
    {
        // Streams alternativos antes de renombrar: el archivo final aparece ya completo.
        if (_options.CopyStreams)
        {
            try
            {
                FileMetadata.CopyAlternateStreams(sourcePath, partialPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"No se copiaron los streams alternativos: {ex.Message}");
            }
        }

        if (_options.PreserveTimestamps)
        {
            Win32.SetTimes(partialPath, info.CreationUtc, info.LastWriteUtc); // las dos de una vez
        }

        if (File.Exists(destinationPath))
        {
            var existing = File.GetAttributes(destinationPath);
            if ((existing & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(destinationPath, existing & ~FileAttributes.ReadOnly);
            }
        }

        // El renombrado es atómico: el archivo final nunca existe a medio copiar.
        File.Move(partialPath, destinationPath, overwrite: true);

        // Permisos antes que atributos: un archivo de solo lectura sigue aceptando cambios de seguridad.
        if (_options.CopySecurity != SecurityParts.None)
        {
            try
            {
                FileMetadata.CopySecurity(sourcePath, destinationPath, _options.CopySecurity);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add(ex.Message);
            }
        }

        if (_options.PreserveAttributes)
        {
            var attributes = info.Attributes & PreservedAttributes;
            File.SetAttributes(destinationPath, attributes == 0 ? FileAttributes.Normal : attributes);
        }
    }

    private SafeFileHandle OpenRead(string path, out bool unbuffered)
    {
        unbuffered = _options.UseUnbufferedIo && IoHelpers.SupportsUnbuffered(path);
        var options = FileOptions.Asynchronous | FileOptions.SequentialScan | (unbuffered ? Win32.NoBuffering : FileOptions.None);
        return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, options);
    }

    private SafeFileHandle OpenWrite(string path, FileMode mode, out bool unbuffered)
    {
        unbuffered = _options.UseUnbufferedIo && IoHelpers.SupportsUnbuffered(path);
        var options = FileOptions.Asynchronous | (unbuffered ? Win32.NoBuffering : FileOptions.None);
        return File.OpenHandle(path, mode, FileAccess.ReadWrite, FileShare.Read, options);
    }

    private static IOException SourceChanged() =>
        new("El archivo de origen cambió de tamaño durante la copia.");

    private readonly record struct IoContext(
        SafeFileHandle Source,
        bool SourceUnbuffered,
        SafeFileHandle Destination,
        bool DestinationUnbuffered);

    private readonly record struct PendingRead(Task<int> Task, AlignedBuffer Buffer, long Offset, int Want);

    /// <summary>
    /// Recibe los bloques en orden, calcula el hash y lanza su escritura sin esperarla (hasta
    /// <see cref="CopyOptions.WriteQueueDepth"/> a la vez). Al completar un bloque espera todas sus escrituras,
    /// vacía al disco y lo confirma en el journal.
    /// </summary>
    private sealed class ChunkWriter(
        FileTransfer owner,
        IoContext io,
        string destinationPath,
        long chunkSize,
        long length,
        List<ChunkRecord> chunks,
        TransferJournal? journal,
        FileHasher? fileHasher)
    {
        private readonly XxHash128 _hasher = new();
        private readonly Queue<(Task Write, AlignedBuffer Buffer)> _inFlight = new();
        private int _chunkLength;

        public async Task AcceptAsync(AlignedBuffer buffer, long offset, int count, CancellationToken cancellationToken)
        {
            try
            {
                await owner._controller.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (owner._throttle is { } throttle)
                {
                    await throttle.WaitAsync(count, cancellationToken).ConfigureAwait(false);
                }

                _hasher.Append(buffer.Span[..count]);
                fileHasher?.Append(buffer.Span[..count]);
                _inFlight.Enqueue((IoHelpers.WriteFullAsync(io.Destination, io.DestinationUnbuffered, buffer, count, offset).AsTask(), buffer));
            }
            catch
            {
                owner._pool.Return(buffer);
                throw;
            }

            long chunkIndex = chunks.Count;
            owner._faults?.Hit(FaultPoint.AfterBlockWrite, chunkIndex, io.Destination);

            _chunkLength += count;
            owner._progress.AddTransferred(count);

            if (_chunkLength == chunkSize || offset + count == length)
            {
                await DrainAsync().ConfigureAwait(false);
                Commit(chunkIndex);
            }
            else
            {
                while (_inFlight.Count >= owner._options.WriteQueueDepth)
                {
                    await CompleteOldestAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Espera las escrituras pendientes tras un error y recupera sus búferes.</summary>
        public async Task AbandonAsync()
        {
            while (_inFlight.TryDequeue(out var item))
            {
                try
                {
                    await item.Write.ConfigureAwait(false);
                }
                catch
                {
                    // Ya se está propagando otro error.
                }

                owner._pool.Return(item.Buffer);
            }
        }

        private async Task DrainAsync()
        {
            while (_inFlight.Count > 0)
            {
                await CompleteOldestAsync().ConfigureAwait(false);
            }
        }

        private async Task CompleteOldestAsync()
        {
            var (write, buffer) = _inFlight.Peek();
            await write.ConfigureAwait(false);
            _inFlight.Dequeue();
            owner._pool.Return(buffer);
        }

        private void Commit(long chunkIndex)
        {
            var record = new ChunkRecord(chunkIndex, _chunkLength, _hasher.GetCurrentHashAsUInt128());
            _hasher.Reset();
            _chunkLength = 0;

            if (journal is not null)
            {
                // Orden crítico: primero los datos en el disco, después la anotación.
                Win32.Flush(io.Destination);
                owner._faults?.Hit(FaultPoint.AfterChunkFlush, chunkIndex, io.Destination);
                journal.Append(record);
                owner._faults?.Hit(FaultPoint.AfterJournalAppend, chunkIndex, io.Destination);
            }

            chunks.Add(record);
            owner._chunkCommitted?.Invoke(destinationPath, (chunkIndex * chunkSize) + record.Length);
        }
    }
}
