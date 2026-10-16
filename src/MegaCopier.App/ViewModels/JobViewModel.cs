using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using MegaCopier.App.Services;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using Microsoft.UI.Dispatching;

namespace MegaCopier.App.ViewModels;

/// <summary>
/// Un trabajo para la interfaz: panel compacto (barra total arriba, archivo actual abajo, unidades de origen y destino),
/// tarjeta de la lista de trabajos y pestañas de la vista completa. Los avisos del motor llegan desde hilos de copia;
/// se agrupan y se refresca como mucho una vez por fotograma.
/// </summary>
public sealed partial class JobViewModel : INotifyPropertyChanged
{
    private readonly CopyQueue _queue;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _hideFinished;
    private int _refreshPending;
    private TransferItem? _current;
    private TransferList? _watched;
    private FileListViewModel? _files;
    private bool _selected;

    internal JobViewModel(CopyJob job, CopyQueue queue, DispatcherQueue dispatcher, Func<bool> hideFinished)
    {
        Job = job;
        _queue = queue;
        _dispatcher = dispatcher;
        _hideFinished = hideFinished;
        job.Changed += _ => ScheduleRefresh();
        job.Logged += (_, entry) => _dispatcher.TryEnqueue(() => Log.Add(new LogRowViewModel(entry)));
        foreach (var entry in job.LogSnapshot())
        {
            Log.Add(new LogRowViewModel(entry));
        }

        WatchFiles();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal CopyJob Job { get; }

    public ObservableCollection<LogRowViewModel> Log { get; } = [];

    /// <summary>Lista de archivos (se crea al abrir la pestaña: un trabajo con 100 000 archivos no la paga si no se mira).</summary>
    public FileListViewModel? Files
    {
        get
        {
            WatchFiles();
            if (_files is null && Job.Files is { } list)
            {
                _files = new FileListViewModel(Job, list, _dispatcher, _hideFinished());
            }

            return _files;
        }
    }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    private CopyJobState State => Job.State;

    private CopyProgress? Progress => Job.Progress;

    private bool IsMove => Job.Record.Move;

    // ---------------------------------------------------------------------------------------------------------
    // Estado

    public bool IsRunning => State == CopyJobState.Running;

    public bool IsPaused => State == CopyJobState.Paused;

    public bool IsQueued => State == CopyJobState.Queued;

    public bool IsDone => State == CopyJobState.Completed;

    public bool IsInterrupted => State == CopyJobState.Interrupted;

    public bool HasProblem => State is CopyJobState.Failed or CopyJobState.CompletedWithErrors;

    public bool IsActive => Job.IsActive;

    public string Glyph => State switch
    {
        CopyJobState.Completed => "\uE73E",                                    // ✓
        CopyJobState.Failed or CopyJobState.CompletedWithErrors => "\uE7BA",   // ⚠
        CopyJobState.Paused or CopyJobState.Interrupted => "\uE769",           // ❚❚
        CopyJobState.Queued => "\uE823",                                       // reloj
        _ => IsMove ? "\uE8DE" : "\uE8C8",                                      // mover / copiar
    };

    public string Title
    {
        get
        {
            var sources = Job.Record.Sources;
            string what = sources.Count == 1
                ? L.F("Job_OneItem", Path.GetFileName(sources[0].TrimEnd('\\')))
                : L.F("Job_ManyItems", sources.Count);
            return State switch
            {
                CopyJobState.Completed => L.F(IsMove ? "Job_TitleMoved" : "Job_TitleCopied", what),
                CopyJobState.Queued => L.F("Job_TitleQueued", what),
                _ => L.F(IsMove ? "Job_TitleMoving" : "Job_TitleCopying", what),
            };
        }
    }

    /// <summary>Título de la ventana y de la tarjeta lateral: "Copiando (51,8 %)".</summary>
    public string ShortTitle => State switch
    {
        CopyJobState.Running or CopyJobState.Paused => $"{L.Get(IsMove ? "Job_StateMoving" : "Job_StateCopying")} ({TotalFraction * 100:0.0} %)",
        _ => StateText,
    };

    /// <summary>Segunda línea de la tarjeta lateral: "→ Destino · 52 MB/s · quedan 10 s" o el estado.</summary>
    public string CardSubtitle
    {
        get
        {
            string target = Path.GetFileName(Path.TrimEndingDirectorySeparator(Job.Record.Destination));
            string status = IsRunning && SpeedAndRemaining.Length > 0 ? SpeedAndRemaining : StateText;
            return $"→ {(target.Length > 0 ? target : Job.Record.Destination)} · {status}";
        }
    }

    public string StateText => State switch
    {
        CopyJobState.Queued => L.Get("Job_StateQueued"),
        CopyJobState.Running when Progress?.Phase == CopyPhase.Verifying => L.Get("Job_StateVerifying"),
        CopyJobState.Running when Progress?.Phase == CopyPhase.Validating => L.Get("Job_StateValidating"),
        CopyJobState.Running when Progress?.Phase == CopyPhase.WaitingForUser => L.Get("Job_StateWaiting"),
        CopyJobState.Running => L.Get(IsMove ? "Job_StateMoving" : "Job_StateCopying"),
        CopyJobState.Paused => L.Get("Job_StatePaused"),
        CopyJobState.Completed => L.Get("Job_StateCompleted"),
        CopyJobState.CompletedWithErrors => L.Get("Job_StateCompletedErrors"),
        CopyJobState.Failed => L.Get("Job_StateFailed"),
        CopyJobState.Interrupted => L.Get("Job_StateInterrupted"),
        _ => "",
    };

    public string DetailText
    {
        get
        {
            if (State == CopyJobState.CompletedWithErrors && Job.Result is { } partial)
            {
                return L.F("Job_FailedFiles", partial.Failures.Count);
            }

            if (State == CopyJobState.Failed)
            {
                return Job.Error ?? "";
            }

            if (State == CopyJobState.Completed && Job.Result is { } result)
            {
                return CompletedSummary(result);
            }

            return State == CopyJobState.Interrupted ? L.Get("Job_ResumeHint") : "";
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Progreso total (barra superior)

    public double TotalFraction => State == CopyJobState.Completed ? 1 : Progress?.Fraction ?? 0;

    public string TotalPercentText => $"{TotalFraction * 100:0.0} %";

    public string TotalBytesText => Progress is { } p
        ? L.F("Job_Progress", Format.Bytes(p.CompletedBytes), Format.Bytes(p.TotalBytes))
        : "";

    public string FilesCountText => Progress is { } p ? $"{Math.Min(p.FilesCompleted + (IsRunning ? 1 : 0), p.TotalFiles)} / {p.TotalFiles}" : "";

    public string TotalSizeText => Progress is { } p ? Format.Bytes(p.TotalBytes) : "";

    public string TotalFilesText => Progress is { } p ? p.TotalFiles.ToString("N0") : "";

    public bool BarIndeterminate => State == CopyJobState.Queued || (IsRunning && Progress is null);

    // ---------------------------------------------------------------------------------------------------------
    // Archivo actual (barra inferior)

    public double FileFraction => Progress is { CurrentFileLength: > 0 } p && IsActive
        ? Math.Clamp((double)p.CurrentFileBytes / p.CurrentFileLength, 0, 1)
        : IsDone ? 1 : 0;

    public string FilePercentText => $"{FileFraction * 100:0.0} %";

    public string CurrentFileName => !IsActive ? "" : _current is { } item ? Path.GetFileName(item.Source) : Path.GetFileName(Progress?.CurrentFile ?? "");

    public string CurrentSourcePath => IsActive && _current is { } item ? item.Source : SourceFolder;

    public string CurrentTargetPath => IsActive && _current is { } item ? item.Destination : Job.Record.Destination;

    public string FileBytesText => Progress is { CurrentFileLength: > 0 } p && IsActive
        ? L.F("Job_Progress", Format.Bytes(p.CurrentFileBytes), Format.Bytes(p.CurrentFileLength))
        : "";

    public string SpeedText => Progress is { BytesPerSecond: > 0 } p && IsRunning ? Format.Speed(p.BytesPerSecond) : "";

    public string RemainingText => Progress is { Remaining: { } remaining } && IsRunning ? L.F("Job_Remaining", Format.Duration(remaining)) : "";

    public string SpeedAndRemaining => string.Join(" · ", new[] { SpeedText, RemainingText }.Where(s => s.Length > 0));

    // ---------------------------------------------------------------------------------------------------------
    // Origen y destino

    public string SourceFolder
    {
        get
        {
            var sources = Job.Record.Sources;
            string first = Path.TrimEndingDirectorySeparator(sources.FirstOrDefault() ?? "");
            return sources.Count == 1 && Directory.Exists(first) ? first + "\\" : (Path.GetDirectoryName(first) ?? first) + "\\";
        }
    }

    public string SourceFolderShort => Format.ShortPath(SourceFolder);

    public string DestinationFolderShort => Format.ShortPath(Job.Record.Destination);

    public string DestinationFolder => Path.TrimEndingDirectorySeparator(Job.Record.Destination) + "\\";

    public string DestinationText => L.F("Job_Destination", Job.Record.Destination);

    public DriveDescription SourceDrive => DriveInfoText.Describe(Job.Record.Sources.FirstOrDefault() ?? "C:\\");

    public DriveDescription DestinationDrive => DriveInfoText.Describe(Job.Record.Destination);

    public string SourceDriveText => SourceDrive.Text;

    public string SourceDriveGlyph => SourceDrive.Glyph;

    public string DestinationDriveText => DestinationDrive.Text;

    public string DestinationDriveGlyph => DestinationDrive.Glyph;

    // ---------------------------------------------------------------------------------------------------------
    // Acciones

    public bool CanPause => State == CopyJobState.Running;

    public bool CanResume => State is CopyJobState.Paused or CopyJobState.Interrupted or CopyJobState.CompletedWithErrors;

    public bool CanSkip => State == CopyJobState.Running;

    public bool CanStop => State is CopyJobState.Running or CopyJobState.Paused or CopyJobState.Queued;

    public bool CanDiscard => State is CopyJobState.Interrupted or CopyJobState.Failed or CopyJobState.CompletedWithErrors;

    public bool CanClear => State is CopyJobState.Completed;

    public string ResumeLabel => L.Get(State == CopyJobState.CompletedWithErrors ? "Job_Retry" : "Job_Resume");

    public bool Unattended
    {
        get => Job.Options?.Unattended ?? Job.Record.Unattended;
        set => Job.SetLiveOptions(unattended: value);
    }

    public bool Verify
    {
        get => Job.Options?.Verify ?? Job.Record.Verify;
        set => Job.SetLiveOptions(verify: value);
    }

    public bool HasFailures => Job.Result is { Failures.Count: > 0 };

    public void Pause() => _queue.Pause(Job);

    public void Resume() => _queue.Resume(Job);

    public void TogglePause()
    {
        if (CanPause)
        {
            Pause();
        }
        else if (CanResume)
        {
            Resume();
        }
    }

    public void Skip() => Job.SkipCurrent();

    public void Stop() => _queue.Stop(Job);

    public void Discard() => _queue.Discard(Job);

    public void Clear() => _queue.Clear(Job);

    public void OpenDestination()
    {
        if (Directory.Exists(Job.Record.Destination))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Job.Record.Destination}\"") { UseShellExecute = true });
        }
    }

    public TransferStatistics Statistics() => TransferStatistics.From(Job.Files?.Snapshot() ?? []);

    public TimeSpan Elapsed => Progress?.Elapsed ?? Job.Result?.Elapsed ?? TimeSpan.Zero;

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private void WatchFiles()
    {
        if (Job.Files is { } list && !ReferenceEquals(list, _watched))
        {
            _watched = list;
            _files?.Dispose();
            _files = null;
            // El primer archivo pudo empezar antes de suscribirse.
            _current = list.Snapshot().FirstOrDefault(i => i.State is FileState.Copying or FileState.Verifying);
            list.Changed += change =>
            {
                if (change.Kind == TransferListChangeKind.Updated && change.Item.State is FileState.Copying or FileState.Verifying)
                {
                    _current = change.Item;
                }
            };
        }
    }

    private string CompletedSummary(CopyJobResult result)
    {
        int files = result.FilesCopied;
        string key = (IsMove, files == 1) switch
        {
            (true, true) => "Job_FilesMovedOne",
            (true, false) => "Job_FilesMovedMany",
            (false, true) => "Job_FilesCopiedOne",
            _ => "Job_FilesCopiedMany",
        };

        var parts = new List<string> { L.F(key, files) };
        if (result.BytesTransferred > 0)
        {
            string transferred = L.F("Job_TransferredIn", Format.Bytes(result.BytesTransferred), Format.Duration(result.Elapsed));
            if (result.Elapsed.TotalSeconds > 0.05 && result.BytesTransferred >= 8 * 1024 * 1024)
            {
                transferred += $" · {Format.Speed(result.BytesTransferred / result.Elapsed.TotalSeconds)}";
            }

            parts.Add(transferred);
        }

        if (result.BytesResumed > 0)
        {
            parts.Add(L.F("Job_Recovered", Format.Bytes(result.BytesResumed)));
        }

        if (result.FilesSkipped > 0)
        {
            parts.Add(L.F("Job_Skipped", result.FilesSkipped));
        }

        if (Job.Record.Verify)
        {
            parts.Add(L.Get("Job_Verified") + (result.ChunksRepaired > 0 ? " " + L.F("Job_Repaired", result.ChunksRepaired) : ""));
        }

        return string.Join(" · ", parts);
    }

    private void ScheduleRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 0)
        {
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Volatile.Write(ref _refreshPending, 0);
                try
                {
                    WatchFiles();
                    Refresh();
                }
                catch (Exception ex)
                {
                    App.LogCrash(ex, "JobViewModel.Refresh");
                    throw;
                }
            });
        }
    }
}
