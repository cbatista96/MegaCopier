using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using MegaCopier.App.Services;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using Microsoft.UI.Dispatching;

namespace MegaCopier.App.ViewModels;

public enum FileFilter
{
    All,
    Pending,
    Done,
    Errors,
    Skipped,
}

/// <summary>Una fila de la lista de archivos.</summary>
public sealed partial class FileRowViewModel(TransferItem item, string root) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    internal TransferItem Item { get; } = item;

    public int Id => Item.Id;

    public string Name => Path.GetFileName(Item.Source);

    /// <summary>Nombre accesible de la fila (lectores de pantalla).</summary>
    public override string ToString() => $"{Name}, {StateText}";

    /// <summary>Carpeta relativa al origen del trabajo (para no repetir la ruta completa en cada fila).</summary>
    public string Folder
    {
        get
        {
            string folder = Path.GetDirectoryName(Item.Source) ?? "";
            return folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? folder[root.Length..].TrimStart('\\') : folder;
        }
    }

    public string SourcePath => Item.Source;

    public string DestinationPath => Item.Destination;

    public string SizeText => Format.Bytes(Item.Length);

    public string SourceHash => Short(Item.SourceHash);

    public string DestinationHash => Short(Item.DestinationHash);

    /// <summary>Motivo claro y traducido; el texto técnico original queda en <see cref="TechnicalMessage"/>.</summary>
    public string Message => Item.State switch
    {
        FileState.Planned => L.Get("Plan_" + Item.Action),
        FileState.Deleted => L.Get("File_RemovedExtra"),
        FileState.Skipped when Item.Action != PlannedAction.None => L.Get("Skip_" + Item.Action),
        _ => Item.ErrorKind == FileErrorKind.Other ? Item.Message ?? "" : L.Get("Error_" + Item.ErrorKind),
    };

    public string TechnicalMessage => Item.Message ?? "";

    public bool HasMessage => !string.IsNullOrEmpty(Item.Message) && Item.State is FileState.Failed or FileState.Skipped or FileState.Planned or FileState.Deleted;

    public bool IsCurrent => Item.State is FileState.Copying or FileState.Verifying;

    public bool IsPending => Item.State == FileState.Pending;

    public bool IsOk => Item.State is FileState.Copied or FileState.Verified;

    public bool IsVerified => Item.State == FileState.Verified;

    public bool IsCopiedOnly => Item.State == FileState.Copied;

    public bool IsSkipped => Item.State == FileState.Skipped;

    public bool IsFailed => Item.State == FileState.Failed;

    public bool IsPlanned => Item.State == FileState.Planned;

    public bool IsDeleted => Item.State == FileState.Deleted;

    /// <summary>Errores en ámbar; simulaciones, omitidos y borrados del destino en gris.</summary>
    public Microsoft.UI.Xaml.Media.Brush MessageBrush => Item.State == FileState.Failed ? Theming.ThemeManager.WarningBrush : Theming.ThemeManager.TextSecondaryBrush;

    public string StateText => Item.State switch
    {
        FileState.Pending => L.Get("File_Pending"),
        FileState.Copying => L.Get("File_Copying"),
        FileState.Verifying => L.Get("File_Verifying"),
        FileState.Copied => L.Get("File_Copied"),
        FileState.Verified => L.Get("File_Verified"),
        FileState.Skipped => L.Get("File_Skipped"),
        FileState.Planned => L.Get("File_Planned"),
        FileState.Deleted => L.Get("File_Deleted"),
        _ => L.Get("File_Failed"),
    };

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    /// <summary>Columnas de hash visibles (se ocultan si la lista es estrecha).</summary>
    public static bool ShowHashes { get; set; } = true;

    public bool HashesVisible => ShowHashes;

    /// <summary>"AE9D:90F6": principio y final del hash, como TeraCopy.</summary>
    private static string Short(string? hash) => hash is { Length: >= 8 } h ? $"{h[..4]}:{h[^4..]}".ToUpperInvariant() : (hash ?? "").ToUpperInvariant();
}

/// <summary>
/// Lista de archivos de un trabajo para la interfaz: filtros, búsqueda, "ocultar terminados" (como Ultracopier,
/// los archivos desaparecen de la lista al copiarse) y ordenar pendientes. Los cambios del motor llegan desde hilos de
/// copia; se agrupan y se aplican al ritmo de la pantalla.
/// </summary>
public sealed partial class FileListViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CopyJob _job;
    private readonly TransferList _list;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly ConcurrentQueue<TransferListChange> _changes = new();
    private readonly Dictionary<int, FileRowViewModel> _rows = [];
    private readonly HashSet<FileRowViewModel> _visible = [];
    private readonly string _root;
    private FileFilter _filter;
    private bool _hideFinished;
    private string _search = "";

    internal FileListViewModel(CopyJob job, TransferList list, DispatcherQueue dispatcher, bool hideFinished)
    {
        _job = job;
        _list = list;
        _dispatcher = dispatcher;
        _hideFinished = hideFinished;
        _root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(job.Record.Sources.FirstOrDefault() ?? "")) ?? "";
        foreach (var item in list.Snapshot())
        {
            _rows[item.Id] = new FileRowViewModel(item, _root);
        }

        Rebuild();
        list.Changed += OnChanged;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(150);
        _timer.Tick += (_, _) => Flush();
        _timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se seleccionó automáticamente el archivo en curso (para el desplazamiento automático).</summary>
    public event Action<FileRowViewModel>? CurrentChanged;

    public ObservableCollection<FileRowViewModel> Items { get; } = [];

    public FileFilter Filter
    {
        get => _filter;
        set
        {
            if (_filter != value)
            {
                _filter = value;
                Rebuild();
            }
        }
    }

    public bool HideFinished
    {
        get => _hideFinished;
        set
        {
            if (_hideFinished != value)
            {
                _hideFinished = value;
                Rebuild();
            }
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (_search != value)
            {
                _search = value ?? "";
                Rebuild();
            }
        }
    }

    public string CountText => L.F("Files_Count", Items.Count, _rows.Count);

    public void MoveToTop(FileRowViewModel row) => Reorder(() => _list.MoveToTop(row.Id));

    public void MoveUp(FileRowViewModel row) => Reorder(() => _list.MoveUp(row.Id));

    public void MoveDown(FileRowViewModel row) => Reorder(() => _list.MoveDown(row.Id));

    public void MoveToBottom(FileRowViewModel row) => Reorder(() => _list.MoveToBottom(row.Id));

    public void Remove(FileRowViewModel row) => _job.RemoveFile(row.Id);

    /// <summary>Hashes de todas las filas visibles en formato de archivo de sumas.</summary>
    public string HashesText() =>
        string.Join("\n", Items.Where(r => r.Item.SourceHash is not null).Select(r => $"{r.Item.SourceHash} *{r.Item.Source}"));

    public void Dispose()
    {
        _timer.Stop();
        _list.Changed -= OnChanged;
    }

    private void Reorder(Func<bool> move)
    {
        if (move())
        {
            Rebuild();
        }
    }

    private void OnChanged(TransferListChange change) => _changes.Enqueue(change);

    private void Flush()
    {
        if (_changes.IsEmpty)
        {
            return;
        }

        bool rebuild = false;
        var updated = new HashSet<FileRowViewModel>();
        var placed = new List<FileRowViewModel>();
        FileRowViewModel? current = null;
        while (_changes.TryDequeue(out var change))
        {
            switch (change.Kind)
            {
                case TransferListChangeKind.Added:
                    var row = new FileRowViewModel(change.Item, _root);
                    _rows[change.Item.Id] = row;
                    if (Matches(row))
                    {
                        _visible.Add(row);
                        if (change.Index >= _list.Count - 1)
                        {
                            Items.Add(row);
                        }
                        else
                        {
                            // Insertado en medio (soltado en una posición concreta).
                            placed.Add(row);
                        }
                    }

                    break;

                case TransferListChangeKind.Removed:
                    if (_rows.Remove(change.Item.Id, out var removed) && _visible.Remove(removed))
                    {
                        Items.Remove(removed);
                    }

                    break;

                case TransferListChangeKind.Moved when _rows.TryGetValue(change.Item.Id, out var moved):
                    if (_visible.Contains(moved))
                    {
                        Items.Remove(moved);
                        placed.Add(moved);
                    }

                    break;

                case TransferListChangeKind.Updated when _rows.TryGetValue(change.Item.Id, out var existing):
                    updated.Add(existing);
                    if (existing.IsCurrent)
                    {
                        current = existing;
                    }

                    break;
            }
        }

        // Pocos movimientos: cada fila a su sitio exacto; muchos: se rehace la lista de una vez.
        if (placed.Count > 50)
        {
            rebuild = true;
        }
        else
        {
            foreach (var row in placed)
            {
                Items.Remove(row);
                InsertInOrder(row);
            }
        }

        if (rebuild)
        {
            Rebuild();
        }
        else
        {
            foreach (var row in updated)
            {
                row.Refresh();
                bool visible = _visible.Contains(row);
                bool matches = Matches(row);
                if (visible && !matches)
                {
                    Items.Remove(row); // "ocultar terminados": desaparece al copiarse
                    _visible.Remove(row);
                }
                else if (!visible && matches)
                {
                    InsertInOrder(row);
                }
            }
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
        if (current is not null)
        {
            CurrentChanged?.Invoke(current);
        }
    }

    private void InsertInOrder(FileRowViewModel row)
    {
        // Posición según el orden de la lista del motor (búsqueda binaria: listas de cientos de miles de archivos).
        var positions = new Dictionary<int, int>();
        var order = _list.Snapshot();
        for (int i = 0; i < order.Count; i++)
        {
            positions[order[i].Id] = i;
        }

        int target = positions.GetValueOrDefault(row.Id, int.MaxValue);
        int low = 0, high = Items.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (positions.GetValueOrDefault(Items[middle].Id, int.MaxValue) < target)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        Items.Insert(low, row);
        _visible.Add(row);
    }

    private void Rebuild()
    {
        Items.Clear();
        _visible.Clear();
        foreach (var item in _list.Snapshot())
        {
            if (_rows.TryGetValue(item.Id, out var row))
            {
                row.Refresh();
                if (Matches(row))
                {
                    Items.Add(row);
                    _visible.Add(row);
                }
            }
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
    }

    private bool Matches(FileRowViewModel row)
    {
        var state = row.Item.State;
        if (_hideFinished && row.Item.IsFinished && state != FileState.Failed)
        {
            return false;
        }

        bool byFilter = _filter switch
        {
            FileFilter.Pending => state is FileState.Pending or FileState.Copying or FileState.Verifying,
            FileFilter.Done => state is FileState.Copied or FileState.Verified,
            FileFilter.Errors => state == FileState.Failed,
            FileFilter.Skipped => state == FileState.Skipped,
            _ => true,
        };

        return byFilter && (_search.Length == 0 || row.Item.Source.Contains(_search, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Una línea del registro.</summary>
public sealed class LogRowViewModel(LogEntry entry)
{
    public string Time => entry.Time.ToString("HH:mm:ss");

    public string Message => entry.Message;

    public string PathText => entry.Path ?? "";

    public bool HasPath => entry.Path is not null;

    public bool IsInfo => entry.Level == LogLevel.Info;

    public bool IsWarning => entry.Level == LogLevel.Warning;

    public bool IsError => entry.Level == LogLevel.Error;
}
