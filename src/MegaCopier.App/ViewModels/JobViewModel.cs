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
    private string? _estimate;

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

    /// <summary>Copy, Move, Test o Delete: parte de las claves de texto ("Job_TitleCopying", "Job_FilesDeletedOne"...).</summary>
    private string Kind => Job.Record.Operation switch
    {
        JobOperation.Test => "Test",
        JobOperation.Delete => "Delete",
        JobOperation.Hash => "Hash",
        JobOperation.Send => "Send",
        JobOperation.Decrypt => "Decrypt",
        JobOperation.Sync => Job.Record.DryRun ? "Simulate" : "Sync",
        _ when Job.Record.Encrypt && !IsMove => "Encrypt",
        _ when Job.Record.DryRun => "Simulate",
        _ when Job.Record.Mode == CopyMode.Mirror && !IsMove => "Sync",
        _ => IsMove ? "Move" : "Copy",
    };

    private string Past => Kind switch
    {
        "Copy" => "Copied", "Move" => "Moved", "Test" => "Tested", "Hash" => "Hashed", "Send" => "Sent", "Decrypt" => "Decrypted", "Encrypt" => "Encrypted", "Simulate" => "Simulated", "Sync" => "Synced", _ => "Deleted",
    };

    private string Progressive => Kind switch
    {
        "Copy" => "Copying", "Move" => "Moving", "Test" => "Testing", "Hash" => "Hashing", "Send" => "Sending", "Decrypt" => "Decrypting", "Encrypt" => "Encrypting", "Simulate" => "Simulating", "Sync" => "Syncing", _ => "Deleting",
    };

    /// <summary>Programada para más tarde.</summary>
    public bool IsScheduled => Job.Record.StartAtUtc is { } at && at > DateTime.UtcNow;

    // ---------------------------------------------------------------------------------------------------------
    // Estado

    public bool IsRunning => State == CopyJobState.Running;

    public bool IsPaused => State == CopyJobState.Paused;

    public bool IsQueued => State == CopyJobState.Queued;

    public bool IsDone => State == CopyJobState.Completed;

    public bool IsInterrupted => State == CopyJobState.Interrupted;

    public bool IsFailed => State == CopyJobState.Failed;

    /// <summary>Terminó, pero con archivos fallidos o se interrumpió.</summary>
    public bool HasWarnings => State is CopyJobState.CompletedWithErrors or CopyJobState.Interrupted;

    public bool HasProblem => State is CopyJobState.Failed or CopyJobState.CompletedWithErrors;

    public bool IsActive => Job.IsActive;

    /// <summary>Color del icono de estado: verde terminado, ámbar con avisos, rojo fallido, acento en curso.</summary>
    public Microsoft.UI.Xaml.Media.Brush StateBrush => State switch
    {
        CopyJobState.Completed => Theming.ThemeManager.SuccessBrush,
        CopyJobState.CompletedWithErrors or CopyJobState.Interrupted => Theming.ThemeManager.WarningBrush,
        CopyJobState.Failed => Theming.ThemeManager.ErrorBrush,
        CopyJobState.Queued or CopyJobState.Paused => Theming.ThemeManager.TextSecondaryBrush,
        _ => Theming.ThemeManager.AccentBrush,
    };

    public string Glyph => State switch
    {
        CopyJobState.Completed => "\uE73E",                                    // ✓
        CopyJobState.Failed or CopyJobState.CompletedWithErrors => "\uE7BA",   // ⚠
        CopyJobState.Paused or CopyJobState.Interrupted => "\uE769",           // ❚❚
        CopyJobState.Queued => "\uE823",                                       // reloj
        _ when Job.Record.Operation == JobOperation.Test => "\uE9D5",           // comprobar
        _ when Job.Record.Operation == JobOperation.Delete => "\uE74D",         // borrar
        _ when Job.Record.Operation == JobOperation.Hash => "\uE8EF",           // crear sumas
        _ when Job.Record.Operation == JobOperation.Send => "\uE724",           // enviar a otro equipo
        _ when Job.Record.DryRun => "\uE7B3",                                   // simular
        _ when Kind == "Sync" => "\uE895",                                      // sincronizar
        _ => IsMove ? "\uE8DE" : "\uE8C8",                                      // mover / copiar
    };

    public string Title
    {
        get
        {
            var sources = Job.Record.Sources;
            string what = sources.Count == 1
                ? L.F("Job_OneItem", Format.ItemName(sources[0]))
                : L.F("Job_ManyItems", sources.Count);
            return State switch
            {
                CopyJobState.Completed or CopyJobState.CompletedWithErrors => L.F("Job_Title" + Past, what),
                CopyJobState.Queued when IsScheduled => L.F("Job_TitleScheduled", what),
                CopyJobState.Queued => L.F("Job_TitleQueued", what),
                CopyJobState.Running or CopyJobState.Paused => L.F("Job_Title" + Progressive, what),
                _ => L.F("Job_Title" + Kind, what),
            };
        }
    }

    /// <summary>Título de la ventana y de la tarjeta lateral: "Copiando (51,8 %)".</summary>
    public string ShortTitle => State switch
    {
        CopyJobState.Running or CopyJobState.Paused => $"{L.Get("Job_State" + Progressive)} ({TotalFraction * 100:0.0} %)",
        _ => StateText,
    };

    /// <summary>Segunda línea de la tarjeta lateral: "→ Destino · 52 MB/s · quedan 10 s" o el estado.</summary>
    public string CardSubtitle
    {
        get
        {
            string target = Format.ItemName(Job.Record.Destination);
            string status = IsRunning && SpeedAndRemaining.Length > 0 ? SpeedAndRemaining : StateText;
            return $"→ {(target.Length > 0 ? target : Format.Display(Job.Record.Destination))} · {status}";
        }
    }

    public string StateText => State switch
    {
        CopyJobState.Queued when IsScheduled => L.F("Job_StateScheduled", Job.Record.StartAtUtc!.Value.ToLocalTime().ToString("g")),
        CopyJobState.Paused when IsScheduled => L.F("Job_StateScheduled", Job.Record.StartAtUtc!.Value.ToLocalTime().ToString("g")),
        CopyJobState.Queued => L.Get("Job_StateQueued") + QueuedEstimate(),
        CopyJobState.Running when Progress?.Phase == CopyPhase.Verifying => L.Get("Job_StateVerifying"),
        CopyJobState.Running when Progress?.Phase == CopyPhase.Validating => L.Get("Job_StateValidating"),
        CopyJobState.Running when Progress?.Phase == CopyPhase.WaitingForUser => L.Get("Job_StateWaiting"),
        CopyJobState.Running when Progress?.Phase == CopyPhase.WaitingForDevice => L.Get("Job_StateWaitingDevice"),
        CopyJobState.Running when Progress?.Phase == CopyPhase.WaitingForSpace => L.Get("Job_StateWaitingSpace"),
        CopyJobState.Running => L.Get("Job_State" + Progressive),
        CopyJobState.Paused when ((App)Microsoft.UI.Xaml.Application.Current).Comfort?.PausedBy(Job) is { } why && why != AutoPauseReason.None => L.Get("Job_StatePaused" + why),
        CopyJobState.Paused => L.Get("Job_StatePaused"),
        CopyJobState.Completed => L.Get("Job_StateCompleted"),
        CopyJobState.CompletedWithErrors => L.Get("Job_StateCompletedErrors"),
        CopyJobState.Failed => L.Get("Job_StateFailed"),
        CopyJobState.Interrupted => L.Get("Job_StateInterrupted"),
        _ => "",
    };

    /// <summary>El chip del panel: el estado sin la estimación (que va en el detalle, con sitio).</summary>
    public string ChipText => State == CopyJobState.Queued && !IsScheduled ? L.Get("Job_StateQueued") : StateText;

    public bool HasDetail => DetailText.Length > 0;

    public string DetailText
    {
        get
        {
            if (State == CopyJobState.CompletedWithErrors && Job.Result is { } partial)
            {
                return partial.Failures.Count == 1 ? L.Get("Job_FailedOne") : L.F("Job_FailedFiles", partial.Failures.Count);
            }

            if (State == CopyJobState.Failed)
            {
                return Job.Error ?? "";
            }

            if (State == CopyJobState.Completed && Job.Result is { } result)
            {
                return CompletedSummary(result);
            }

            if (State == CopyJobState.Running && Job.SpeedOverride is not null)
            {
                return L.Get(((App)Microsoft.UI.Xaml.Application.Current).Comfort?.SlowReason == "Schedule" ? "Job_SlowedSchedule" : "Job_SlowedDown");
            }

            if (State == CopyJobState.Queued && !IsScheduled && QueuedEstimate() is { Length: > 3 } estimate)
            {
                return estimate[3..]; // sin el " · " del principio
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

    public bool BarIndeterminate => (State == CopyJobState.Queued && !IsScheduled) || (IsRunning && Progress is null);

    // ---------------------------------------------------------------------------------------------------------
    // Archivo actual (barra inferior)

    public double FileFraction => Progress is { CurrentFileLength: > 0 } p && IsActive
        ? Math.Clamp((double)p.CurrentFileBytes / p.CurrentFileLength, 0, 1)
        : IsDone || State == CopyJobState.CompletedWithErrors ? 1 : 0;

    public string FilePercentText => $"{FileFraction * 100:0.0} %";

    public string CurrentFileName => !IsActive ? "" : _current is { } item ? Path.GetFileName(item.Source) : Path.GetFileName(Progress?.CurrentFile ?? "");

    public string CurrentSourcePath => IsActive && _current is { } item ? Format.Display(item.Source) : SourceFolder;

    public string CurrentTargetPath => Format.Display(IsActive && _current is { } item ? item.Destination : Job.Record.Destination);

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
            if (Engine.Mtp.MtpPath.IsMtp(first))
            {
                return Format.Display(sources.Count == 1 ? first : Engine.Mtp.MtpPath.Parent(first)) + "\\";
            }

            return sources.Count == 1 && Directory.Exists(first) ? first + "\\" : (Path.GetDirectoryName(first) ?? first) + "\\";
        }
    }

    /// <summary>Carpetas finales que se ven en las rutas del panel (1 si la ventana es muy estrecha).</summary>
    public static int PathKeep { get; set; } = 2;

    public string SourceFolderShort => Format.ShortPath(SourceFolder, PathKeep);

    public string DestinationFolderShort => Format.ShortPath(Job.Record.Destination, PathKeep);

    public string DestinationFolder => Path.TrimEndingDirectorySeparator(Format.Display(Job.Record.Destination)) + "\\";

    public string DestinationText => L.F("Job_Destination", Format.Display(Job.Record.Destination));

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

    /// <summary>Continuar. Una copia cifrada (o un descifrado) que perdió la contraseña al reiniciar la pide antes.</summary>
    public async void Resume()
    {
        if ((Job.Record.Encrypt || Job.Record.Operation == JobOperation.Decrypt) && Job.Password.Length == 0)
        {
            var app = (App)Microsoft.UI.Xaml.Application.Current;
            if (await app.Window.AskPasswordAsync(L.Get("Resume_PasswordAsk")) is not { } password)
            {
                return;
            }

            Job.Password = password;
        }

        _queue.Resume(Job);
    }

    /// <summary>La simulación terminó: hacer la misma copia de verdad.</summary>
    public bool CanRunForReal => Job.Record.DryRun && State is CopyJobState.Completed or CopyJobState.CompletedWithErrors;

    public void RunForReal() => _queue.RunForReal(Job);

    /// <summary>Terminó con archivos que otro programa tenía en uso: se pueden copiar desde una instantánea.</summary>
    public bool CanCopyLocked => State == CopyJobState.CompletedWithErrors && Job.Result is { } r && r.Failures.Any(f => f.Kind == FileErrorKind.InUse);

    public void CopyLocked() => _ = ((App)Microsoft.UI.Xaml.Application.Current).CopyLockedFilesAsync(Job);

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

    /// <summary>Programa el inicio (hora local; null = empezar ahora).</summary>
    public void Schedule(DateTime? startLocal)
    {
        _queue.ScheduleAt(Job, startLocal?.ToUniversalTime());
        Refresh();
    }

    public bool CanSchedule => State is CopyJobState.Queued or CopyJobState.Running or CopyJobState.Paused or CopyJobState.Interrupted;

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
        if (files == 0 && result.FilesSkipped == 0 && result.Failures.Count == 0 && !Job.Record.Filter.IsEmpty)
        {
            return L.Get("Job_FilteredOut");
        }

        string key = "Job_Files" + Past + (files == 1 ? "One" : "Many");

        var parts = new List<string> { L.F(key, files) };
        if (Job.Record.DryRun)
        {
            if (result.BytesTransferred > 0)
            {
                parts.Add(L.F("Job_WouldTransfer", Format.Bytes(result.BytesTransferred)));
            }
        }
        else if (result.BytesTransferred > 0)
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
            parts.Add(result.FilesSkipped == 1 ? L.Get("Job_SkippedOne") : L.F("Job_Skipped", result.FilesSkipped));
        }

        if (result.FilesDeleted > 0)
        {
            parts.Add(Job.Record.DryRun ? L.F("Job_WouldRemove", result.FilesDeleted)
                : result.FilesDeleted == 1 ? L.Get("Job_RemovedOne") : L.F("Job_Removed", result.FilesDeleted));
        }

        if (Job.Record.Verify)
        {
            parts.Add(L.Get("Job_Verified") + (result.ChunksRepaired > 0 ? " " + L.F("Job_Repaired", result.ChunksRepaired) : ""));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// En cola, antes de empezar: " · 5,2 GB, unos 3 min". El tamaño se calcula aparte (una vez) y la velocidad sale de
    /// las últimas copias al mismo disco en el Historial (o de cualquier copia, si no hay ninguna a ese disco).
    /// </summary>
    private string QueuedEstimate()
    {
        if (_estimate is not null)
        {
            return _estimate;
        }

        _estimate = "";
        var sources = Job.Record.Sources.ToList();
        string destination = Job.Record.Destination;
        _ = Task.Run(() =>
        {
            long bytes = 0;
            int files = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string source in sources)
            {
                if (File.Exists(source))
                {
                    bytes += new FileInfo(source).Length;
                    files++;
                    continue;
                }

                if (!Directory.Exists(source))
                {
                    continue;
                }

                foreach (var file in new DirectoryInfo(source).EnumerateFiles("*", options))
                {
                    bytes += file.Length;
                    if (++files > 200_000)
                    {
                        return; // demasiado grande para estimarlo sin retrasar nada
                    }
                }
            }

            string root = Path.GetPathRoot(destination) ?? "";
            var past = Services.History.Load().Where(h => h.Bytes > 16 * 1024 * 1024 && h.Seconds > 0.5 && h.Operation == JobOperation.Copy).ToList();
            var sameDisk = past.Where(h => string.Equals(Path.GetPathRoot(h.Destination), root, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();
            var sample = sameDisk.Count > 0 ? sameDisk : past.Take(20).ToList();
            string size = Format.Bytes(bytes);
            string text = sample.Count > 0 && bytes > 0
                ? L.F("Job_Estimate", size, Format.Duration(TimeSpan.FromSeconds(bytes / (sample.Sum(h => (double)h.Bytes) / sample.Sum(h => h.Seconds)))))
                : bytes > 0 ? L.F("Job_EstimateSize", size) : "";
            _estimate = text.Length > 0 ? " · " + text : "";
            ScheduleRefresh();
        });
        return _estimate;
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
