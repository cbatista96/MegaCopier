using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine;

public sealed record CopyJobRequest(IReadOnlyList<string> Sources, string Destination)
{
    /// <summary>Archivos que el usuario quitó de la lista: no se copian (también al reanudar).</summary>
    public IReadOnlyCollection<string> Excluded { get; init; } = [];

    /// <summary>Orden elegido por el usuario para los pendientes (rutas de origen); lo demás va detrás.</summary>
    public IReadOnlyList<string> Order { get; init; } = [];

    /// <summary>El trabajo ya empezó antes (se está reanudando): la regla de carpetas existentes no se vuelve a aplicar.</summary>
    public bool Resumed { get; init; }

    /// <summary>Carpetas de origen que se copian con otro nombre (ruta de origen → nombre nuevo en el destino).</summary>
    public IReadOnlyDictionary<string, string> RenamedFolders { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Orígenes que se sumaron a esta copia hacia otra carpeta (ruta de origen → carpeta de destino): una copia nueva al
    /// mismo disco añadida a la que ya estaba en marcha. Se copian después de los orígenes propios.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtraTargets { get; init; } = new Dictionary<string, string>();
}

public enum CopyPhase
{
    Preparing,
    Validating,
    Copying,
    Verifying,

    /// <summary>Esperando a que el usuario decida un conflicto.</summary>
    WaitingForUser,

    /// <summary>Esperando a que vuelva la red o la unidad.</summary>
    WaitingForDevice,

    /// <summary>Esperando a que haya espacio en el destino.</summary>
    WaitingForSpace,
    Completed,
}

public enum ConflictDecision
{
    Replace,
    Skip,
    KeepBoth,

    /// <summary>Renombrar el que ya estaba y copiar con el nombre original.</summary>
    RenameExisting,
}

/// <summary>Un archivo de destino ya existe y es distinto del origen.</summary>
public sealed record ConflictInfo(
    string Source,
    string Destination,
    long SourceLength,
    DateTime SourceLastWriteUtc,
    long DestinationLength,
    DateTime DestinationLastWriteUtc);

/// <param name="ApplyToAll">Usar la misma respuesta en el resto de conflictos de este trabajo.</param>
public sealed record ConflictAnswer(ConflictDecision Decision, bool ApplyToAll);

/// <summary>
/// Carpetas de origen que ya existen en el destino con algunos de sus archivos (para preguntar qué hacer antes de empezar).
/// </summary>
/// <param name="Folders">Cuántas carpetas de origen ya existen en el destino.</param>
/// <param name="Files">Archivos de esas carpetas que se van a copiar.</param>
/// <param name="ExistingFiles">De ellos, cuántos ya están en el destino.</param>
public sealed record FolderConflictInfo(string Source, string Destination, int Folders, int Files, int ExistingFiles, long ExistingBytes);

public enum FolderDecision
{
    /// <summary>Pasar: los archivos que ya están no se copian ni se tocan; se copia el resto.</summary>
    SkipExisting,

    /// <summary>Sobrescribir los que ya están (los idénticos se omiten igualmente).</summary>
    Overwrite,

    /// <summary>Copiar la carpeta con otro nombre: «Fotos (2)».</summary>
    Rename,

    /// <summary>Preguntar archivo por archivo.</summary>
    AskEach,

    /// <summary>No copiar nada.</summary>
    Cancel,
}

/// <summary>La copia no se hizo porque el usuario la canceló al ver que la carpeta ya existía.</summary>
public sealed class FolderConflictCancelledException : OperationCanceledException;

/// <summary>La copia no se hizo porque no cabía en el destino y el usuario prefirió no forzarla.</summary>
public sealed class SpaceCancelledException : OperationCanceledException;

/// <summary>Lo que se va a copiar no cabe en el destino: cuánto hace falta y cuánto hay libre.</summary>
public sealed record SpaceShortage(string Destination, long Needed, long Free);

public readonly record struct CopyProgress(
    CopyPhase Phase,
    string CurrentFile,
    long CurrentFileBytes,
    long CurrentFileLength,
    long CompletedBytes,
    long TotalBytes,
    int FilesCompleted,
    int TotalFiles,
    double BytesPerSecond,
    TimeSpan Elapsed)
{
    public double Fraction => TotalBytes == 0 ? 1 : Math.Clamp((double)CompletedBytes / TotalBytes, 0, 1);

    public TimeSpan? Remaining => BytesPerSecond > 0
        ? TimeSpan.FromSeconds((TotalBytes - CompletedBytes) / BytesPerSecond)
        : null;
}

public sealed record FileFailure(string Source, string Message, FileErrorKind Kind = FileErrorKind.Other);

public sealed class CopyJobResult
{
    public int FilesCopied { get; internal set; }

    public int FilesResumed { get; internal set; }

    public int FilesSkipped { get; internal set; }

    /// <summary>Archivos borrados del destino por sincronizar (o que se borrarían, al simular).</summary>
    public int FilesDeleted { get; internal set; }

    public List<FileFailure> Failures { get; } = [];

    /// <summary>Bytes realmente leídos y escritos en esta ejecución.</summary>
    public long BytesTransferred { get; internal set; }

    /// <summary>Bytes que no hubo que volver a copiar gracias a la reanudación.</summary>
    public long BytesResumed { get; internal set; }

    /// <summary>Bloques confirmados que al reanudar no coincidían con su hash y se volvieron a copiar.</summary>
    public int ChunksRejectedOnResume { get; internal set; }

    /// <summary>Bloques que la verificación final encontró dañados y reparó.</summary>
    public int ChunksRepaired { get; internal set; }

    public TimeSpan Elapsed { get; internal set; }

    public bool Success => Failures.Count == 0;

    /// <summary>Suma lo que hizo otro hilo de copia del mismo trabajo.</summary>
    internal void Add(CopyJobResult other)
    {
        FilesCopied += other.FilesCopied;
        FilesResumed += other.FilesResumed;
        FilesSkipped += other.FilesSkipped;
        FilesDeleted += other.FilesDeleted;
        Failures.AddRange(other.Failures);
        BytesTransferred += other.BytesTransferred;
        BytesResumed += other.BytesResumed;
        ChunksRejectedOnResume += other.ChunksRejectedOnResume;
        ChunksRepaired += other.ChunksRepaired;
    }
}

/// <summary>Error al preparar el trabajo (origen inexistente, copia recursiva, sin espacio...). No se reintenta.</summary>
public class CopyPlanException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>El destino no coincide con el origen ni siquiera después de repararlo.</summary>
public class CopyVerificationException(string message) : IOException(message);

/// <summary>Pausa y reanudación en caliente de un trabajo en curso.</summary>
public sealed class CopyController
{
    private readonly Lock _lock = new();
    private TaskCompletionSource? _paused;

    public bool IsPaused => Volatile.Read(ref _paused) is not null;

    public void Pause()
    {
        lock (_lock)
        {
            _paused ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource? paused;
        lock (_lock)
        {
            paused = _paused;
            _paused = null;
        }

        paused?.TrySetResult();
    }

    internal void WaitIfPaused(CancellationToken cancellationToken) =>
        WaitIfPausedAsync(cancellationToken).GetAwaiter().GetResult();

    internal Task WaitIfPausedAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _paused) is { } paused ? paused.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
}

internal enum FaultPoint
{
    AfterBlockWrite,
    AfterChunkFlush,
    AfterJournalAppend,
    BeforeVerify,
}

/// <summary>Puntos de inyección de fallos para probar la recuperación (solo pruebas).</summary>
internal interface IFaultInjector
{
    void Hit(FaultPoint point, long chunkIndex, SafeFileHandle destination);

    /// <summary>Antes de leer del origen (para simular sectores dañados).</summary>
    void BeforeRead(long offset, int length)
    {
    }
}

/// <summary>Por qué falló un archivo, para mostrar un mensaje claro y traducido (el texto técnico va aparte).</summary>
public enum FileErrorKind
{
    Other,
    InUse,
    AccessDenied,
    DiskFull,
    NotFound,
    PathTooLong,
    NetworkLost,
    HashMismatch,

    /// <summary>Comprobar sumas: el archivo no coincide con la suma guardada.</summary>
    ChecksumMismatch,

    /// <summary>El archivo es demasiado grande para el sistema de archivos del destino (FAT32: 4 GB).</summary>
    TooLargeForFileSystem,

    /// <summary>Modo rescate: se copió todo lo legible; partes ilegibles del origen quedaron en ceros.</summary>
    PartlyUnreadable,
    DeleteFailed,
}

public static class FileErrors
{
    public static FileErrorKind Classify(Exception exception) => exception switch
    {
        UnauthorizedAccessException => FileErrorKind.AccessDenied,
        PathTooLongException => FileErrorKind.PathTooLong,
        FileNotFoundException or DirectoryNotFoundException => FileErrorKind.NotFound,
        IOException io => (io.HResult & 0xFFFF) switch
        {
            32 or 33 => FileErrorKind.InUse,                        // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION
            5 => FileErrorKind.AccessDenied,
            39 or 112 => FileErrorKind.DiskFull,                     // ERROR_HANDLE_DISK_FULL, ERROR_DISK_FULL
            2 or 3 or 15 or 21 => FileErrorKind.NotFound,            // archivo, ruta, unidad, unidad no lista
            206 => FileErrorKind.PathTooLong,
            53 or 59 or 64 or 67 or 1231 or 1236 => FileErrorKind.NetworkLost,
            _ => FileErrorKind.Other,
        },
        _ => FileErrorKind.Other,
    };
}
