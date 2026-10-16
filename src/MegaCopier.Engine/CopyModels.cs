using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine;

public sealed record CopyJobRequest(IReadOnlyList<string> Sources, string Destination)
{
    /// <summary>Archivos que el usuario quitó de la lista: no se copian (también al reanudar).</summary>
    public IReadOnlyCollection<string> Excluded { get; init; } = [];

    /// <summary>Orden elegido por el usuario para los pendientes (rutas de origen); lo demás va detrás.</summary>
    public IReadOnlyList<string> Order { get; init; } = [];
}

public enum CopyPhase
{
    Preparing,
    Validating,
    Copying,
    Verifying,

    /// <summary>Esperando a que el usuario decida un conflicto.</summary>
    WaitingForUser,
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
