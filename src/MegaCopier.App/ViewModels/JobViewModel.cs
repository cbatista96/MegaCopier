using System.ComponentModel;
using System.Diagnostics;
using MegaCopier.App.Services;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using Microsoft.UI.Dispatching;

namespace MegaCopier.App.ViewModels;

/// <summary>
/// Tarjeta de un trabajo. El trabajo avisa desde hilos de copia; aquí se agrupan los avisos y se refresca
/// la interfaz como mucho una vez por fotograma en el hilo de la interfaz.
/// </summary>
public sealed partial class JobViewModel : INotifyPropertyChanged
{
    private readonly CopyQueue _queue;
    private readonly DispatcherQueue _dispatcher;
    private int _refreshPending;

    internal JobViewModel(CopyJob job, CopyQueue queue, DispatcherQueue dispatcher)
    {
        Job = job;
        _queue = queue;
        _dispatcher = dispatcher;
        job.Changed += _ => ScheduleRefresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal CopyJob Job { get; }

    private CopyJobState State => Job.State;

    private CopyProgress? Progress => Job.Progress;

    private bool IsMove => Job.Record.Move;

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

    public string DestinationText => L.F("Job_Destination", Job.Record.Destination);

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

            if (Progress is not { } p)
            {
                return State == CopyJobState.Interrupted ? L.Get("Job_ResumeHint") : L.Get("Job_Preparing");
            }

            string text = L.F("Job_Progress", Format.Bytes(p.CompletedBytes), Format.Bytes(p.TotalBytes));
            if (State == CopyJobState.Running && p.BytesPerSecond > 0)
            {
                text += $" · {Format.Speed(p.BytesPerSecond)}";
                if (p.Remaining is { } remaining)
                {
                    text += " · " + L.F("Job_Remaining", Format.Duration(remaining));
                }
            }

            return text + " · " + L.F("Job_Files", p.FilesCompleted, p.TotalFiles);
        }
    }

    public string CurrentFileText =>
        State is CopyJobState.Running or CopyJobState.Paused && Progress is { CurrentFile.Length: > 0 } p
            ? Path.GetFileName(p.CurrentFile)
            : "";

    public double Percent => State == CopyJobState.Completed ? 100 : (Progress?.Fraction ?? 0) * 100;

    public string PercentText => State is CopyJobState.Running or CopyJobState.Paused or CopyJobState.Interrupted && Progress is not null
        ? $"{Percent:0}%"
        : "";

    public bool IsIndeterminate => State == CopyJobState.Queued || (State == CopyJobState.Running && Progress is null);

    public bool ShowPaused => State is CopyJobState.Paused or CopyJobState.Interrupted;

    public bool ShowError => State is CopyJobState.Failed or CopyJobState.CompletedWithErrors;

    public bool CanPause => State == CopyJobState.Running;

    public bool CanResume => State is CopyJobState.Paused or CopyJobState.Interrupted or CopyJobState.CompletedWithErrors;

    public string ResumeLabel => L.Get(State == CopyJobState.CompletedWithErrors ? "Job_Retry" : "Job_Resume");

    public bool CanStop => State is CopyJobState.Running or CopyJobState.Paused or CopyJobState.Queued;

    public bool CanDiscard => State is CopyJobState.Interrupted or CopyJobState.Failed or CopyJobState.CompletedWithErrors;

    public bool CanClear => State == CopyJobState.Completed;

    public bool HasFailures => Job.Result is { Failures.Count: > 0 };

    public string FailuresHeader => Job.Result is { } r ? L.F("Job_FailuresHeader", r.Failures.Count) : "";

    /// <summary>Un archivo por línea: nombre y motivo.</summary>
    public string FailuresText => Job.Result is { } result
        ? string.Join("\n", result.Failures.Take(200).Select(f => $"{Path.GetFileName(f.Source)} — {f.Message}"))
          + (result.Failures.Count > 200 ? "\n" + L.F("Job_FailuresMore", result.Failures.Count - 200) : "")
        : "";

    public string Glyph => State switch
    {
        CopyJobState.Completed => "",          // CheckMark
        CopyJobState.Failed or CopyJobState.CompletedWithErrors => "", // Error
        CopyJobState.Paused or CopyJobState.Interrupted => "",         // Pause
        CopyJobState.Queued => "",            // Recent
        _ => IsMove ? "" : "",           // MoveToFolder / Copy
    };

    public void Pause() => _queue.Pause(Job);

    public void Resume() => _queue.Resume(Job);

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

            // La velocidad solo tiene sentido con un volumen apreciable (y sin esperas largas del usuario de por medio).
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // refresca todas las propiedades
            });
        }
    }
}
