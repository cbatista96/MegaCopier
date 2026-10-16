namespace MegaCopier.Engine;

public enum FileState
{
    Pending,
    Copying,
    Verifying,

    /// <summary>Copiado (sin verificar).</summary>
    Copied,

    /// <summary>Copiado y verificado: el hash del destino coincide con el del origen.</summary>
    Verified,
    Skipped,
    Failed,
}

/// <summary>Un archivo del trabajo, observable desde la interfaz.</summary>
public sealed class TransferItem
{
    internal TransferItem(int id, string source, string destination, long length, DateTime lastWriteUtc)
    {
        Id = id;
        Source = source;
        Destination = destination;
        Length = length;
        LastWriteUtc = lastWriteUtc;
    }

    public int Id { get; }

    public string Source { get; }

    /// <summary>Destino previsto; puede cambiar (por ejemplo, "nombre (2).ext" al conservar ambos).</summary>
    public string Destination { get; internal set; }

    public long Length { get; }

    public DateTime LastWriteUtc { get; }

    public FileState State { get; internal set; }

    public string? SourceHash { get; internal set; }

    public string? DestinationHash { get; internal set; }

    /// <summary>Motivo del error, o explicación de por qué se omitió.</summary>
    public string? Message { get; internal set; }

    public FileErrorKind ErrorKind { get; internal set; }

    public bool IsFinished => State is FileState.Copied or FileState.Verified or FileState.Skipped or FileState.Failed;
}

public enum TransferListChangeKind
{
    Added,
    Updated,
    Moved,
    Removed,
}

public readonly record struct TransferListChange(TransferListChangeKind Kind, TransferItem Item, int Index);

/// <summary>
/// Lista de archivos de un trabajo. El motor toma los pendientes en orden; mientras tanto la interfaz puede
/// reordenar o quitar pendientes y el trabajo puede recibir más archivos. Es segura entre hilos.
/// </summary>
public sealed class TransferList
{
    private readonly Lock _lock = new();
    private readonly List<TransferItem> _items = [];
    private int _nextId;

    /// <summary>Cambios de la lista (desde hilos de copia o de la interfaz).</summary>
    public event Action<TransferListChange>? Changed;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public IReadOnlyList<TransferItem> Snapshot()
    {
        lock (_lock)
        {
            return [.. _items];
        }
    }

    /// <summary>Bytes y archivos que quedan en la lista (para los totales del progreso).</summary>
    public (long Bytes, int Files) Totals()
    {
        lock (_lock)
        {
            return (_items.Sum(i => i.Length), _items.Count);
        }
    }

    public bool MoveToTop(int id) => Move(id, (_, _) => FirstPendingIndex());

    public bool MoveToBottom(int id) => Move(id, (_, _) => _items.Count - 1);

    public bool MoveUp(int id) => Move(id, (index, _) => Math.Max(FirstPendingIndex(), index - 1));

    public bool MoveDown(int id) => Move(id, (index, _) => Math.Min(_items.Count - 1, index + 1));

    /// <summary>Quita un archivo pendiente (no se copiará).</summary>
    public bool Remove(int id)
    {
        TransferItem item;
        int index;
        lock (_lock)
        {
            index = _items.FindIndex(i => i.Id == id);
            if (index < 0 || _items[index].State != FileState.Pending)
            {
                return false;
            }

            item = _items[index];
            _items.RemoveAt(index);
        }

        Changed?.Invoke(new TransferListChange(TransferListChangeKind.Removed, item, index));
        return true;
    }

    internal TransferItem Add(string source, string destination, long length, DateTime lastWriteUtc)
    {
        TransferItem item;
        int index;
        lock (_lock)
        {
            item = new TransferItem(++_nextId, source, destination, length, lastWriteUtc);
            _items.Add(item);
            index = _items.Count - 1;
        }

        Changed?.Invoke(new TransferListChange(TransferListChangeKind.Added, item, index));
        return item;
    }

    /// <summary>El siguiente pendiente en el orden actual (o null si no queda ninguno), ya marcado como en curso.</summary>
    internal TransferItem? TakeNext()
    {
        TransferItem? item;
        int index;
        lock (_lock)
        {
            index = _items.FindIndex(i => i.State == FileState.Pending);
            if (index < 0)
            {
                return null;
            }

            item = _items[index];
            item.State = FileState.Copying;
        }

        Changed?.Invoke(new TransferListChange(TransferListChangeKind.Updated, item, index));
        return item;
    }

    internal void Update(TransferItem item, FileState state, string? message = null, FileErrorKind kind = FileErrorKind.Other)
    {
        int index;
        lock (_lock)
        {
            item.State = state;
            item.ErrorKind = kind;
            if (message is not null)
            {
                item.Message = message;
            }

            index = _items.IndexOf(item);
        }

        Changed?.Invoke(new TransferListChange(TransferListChangeKind.Updated, item, index));
    }

    private int FirstPendingIndex()
    {
        int index = _items.FindIndex(i => i.State == FileState.Pending);
        return index < 0 ? _items.Count - 1 : index;
    }

    /// <summary>Solo se mueven pendientes y nunca por delante de lo ya empezado.</summary>
    private bool Move(int id, Func<int, TransferItem, int> target)
    {
        TransferItem item;
        int to;
        lock (_lock)
        {
            int from = _items.FindIndex(i => i.Id == id);
            if (from < 0 || _items[from].State != FileState.Pending)
            {
                return false;
            }

            item = _items[from];
            to = Math.Clamp(target(from, item), 0, _items.Count - 1);
            if (to == from)
            {
                return false;
            }

            _items.RemoveAt(from);
            _items.Insert(to, item);
        }

        Changed?.Invoke(new TransferListChange(TransferListChangeKind.Moved, item, to));
        return true;
    }
}

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message, string? Path = null);

/// <summary>Resumen por categorías (pestaña Estado).</summary>
public sealed record TransferStatistics(
    int CopiedFiles, long CopiedBytes,
    int VerifiedFiles, long VerifiedBytes,
    int SkippedFiles, long SkippedBytes,
    int FailedFiles, long FailedBytes,
    int PendingFiles, long PendingBytes)
{
    public static TransferStatistics From(IReadOnlyList<TransferItem> items)
    {
        int Count(FileState s) => items.Count(i => i.State == s);
        long Bytes(FileState s) => items.Where(i => i.State == s).Sum(i => i.Length);
        return new TransferStatistics(
            Count(FileState.Copied), Bytes(FileState.Copied),
            Count(FileState.Verified), Bytes(FileState.Verified),
            Count(FileState.Skipped), Bytes(FileState.Skipped),
            Count(FileState.Failed), Bytes(FileState.Failed),
            items.Count(i => !i.IsFinished), items.Where(i => !i.IsFinished).Sum(i => i.Length));
    }
}
