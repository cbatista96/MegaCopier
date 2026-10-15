using System.ComponentModel;
using System.Diagnostics;
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

    public string Title
    {
        get
        {
            var sources = Job.Record.Sources;
            string what = sources.Count == 1 ? $"«{Path.GetFileName(sources[0].TrimEnd('\\'))}»" : $"{sources.Count} elementos";
            bool move = Job.Record.Move;
            return State switch
            {
                CopyJobState.Completed => move ? $"Movido {what}" : $"Copiado {what}",
                CopyJobState.Queued => $"{Capitalize(what)} en espera",
                _ => move ? $"Moviendo {what}" : $"Copiando {what}",
            };
        }
    }

    public string DestinationText => $"a {Job.Record.Destination}";

    public string StateText => State switch
    {
        CopyJobState.Queued => "En cola · espera a que el disco quede libre",
        CopyJobState.Running when Progress?.Phase == CopyPhase.Verifying => "Verificando",
        CopyJobState.Running when Progress?.Phase == CopyPhase.Validating => "Comprobando lo ya copiado",
        CopyJobState.Running when Progress?.Phase == CopyPhase.WaitingForUser => "Esperando tu decisión: el archivo ya existe",
        CopyJobState.Running => Job.Record.Move ? "Moviendo" : "Copiando",
        CopyJobState.Paused => "En pausa",
        CopyJobState.Completed => "Completado",
        CopyJobState.CompletedWithErrors => "Completado con errores",
        CopyJobState.Failed => "Error",
        CopyJobState.Interrupted => "Interrumpido · se puede continuar desde donde quedó",
        _ => "",
    };

    public string DetailText
    {
        get
        {
            if (State is CopyJobState.Failed or CopyJobState.CompletedWithErrors && Job.Error is { } error)
            {
                var failures = Job.Result?.Failures;
                return failures is { Count: > 0 } ? $"{error}: {failures[0].Message}" : error;
            }

            if (State == CopyJobState.Completed && Job.Result is { } result)
            {
                // La velocidad solo tiene sentido con un volumen apreciable (y sin esperas largas del usuario de por medio).
                string speed = result.Elapsed.TotalSeconds > 0.05 && result.BytesTransferred >= 8 * 1024 * 1024
                    ? $" · {Format.Speed(result.BytesTransferred / result.Elapsed.TotalSeconds)}"
                    : "";
                string resumed = result.BytesResumed > 0 ? $" · {Format.Bytes(result.BytesResumed)} recuperados" : "";
                string skipped = result.FilesSkipped > 0 ? $" · {result.FilesSkipped} omitidos" : "";
                string verified = Job.Record.Verify ? " · verificado" : "";
                string repaired = result.ChunksRepaired > 0 ? $" ({result.ChunksRepaired} bloques reparados)" : "";
                bool one = result.FilesCopied == 1;
                string verb = (Job.Record.Move, one) switch
                {
                    (true, true) => "archivo movido",
                    (true, false) => "archivos movidos",
                    (false, true) => "archivo copiado",
                    _ => "archivos copiados",
                };
                string transferred = result.BytesTransferred > 0 ? $" · {Format.Bytes(result.BytesTransferred)} en {Format.Duration(result.Elapsed)}{speed}" : "";
                return $"{result.FilesCopied} {verb}{transferred}{resumed}{skipped}{verified}{repaired}";
            }

            if (Progress is not { } p)
            {
                return State == CopyJobState.Interrupted ? "Pulsa Continuar para seguir copiando" : "Preparando…";
            }

            string text = $"{Format.Bytes(p.CompletedBytes)} de {Format.Bytes(p.TotalBytes)}";
            if (State == CopyJobState.Running && p.BytesPerSecond > 0)
            {
                text += $" · {Format.Speed(p.BytesPerSecond)}";
                if (p.Remaining is { } remaining)
                {
                    text += $" · quedan {Format.Duration(remaining)}";
                }
            }

            return text + $" · {p.FilesCompleted} de {p.TotalFiles} archivos";
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

    public string ResumeLabel => State == CopyJobState.CompletedWithErrors ? "Reintentar" : "Continuar";

    public bool CanStop => State is CopyJobState.Running or CopyJobState.Paused or CopyJobState.Queued;

    public bool CanDiscard => State is CopyJobState.Interrupted or CopyJobState.Failed or CopyJobState.CompletedWithErrors;

    public bool CanClear => State == CopyJobState.Completed;

    public bool HasFailures => Job.Result is { Failures.Count: > 0 };

    public string FailuresHeader => Job.Result is { } r ? $"Ver los {r.Failures.Count} archivos con problemas" : "";

    /// <summary>Un archivo por línea: nombre y motivo.</summary>
    public string FailuresText => Job.Result is { } result
        ? string.Join("\n", result.Failures.Take(200).Select(f => $"{Path.GetFileName(f.Source)} — {f.Message}"))
          + (result.Failures.Count > 200 ? $"\n… y {result.Failures.Count - 200} más" : "")
        : "";

    public string Glyph => State switch
    {
        CopyJobState.Completed => "",          // CheckMark
        CopyJobState.Failed or CopyJobState.CompletedWithErrors => "", // Error
        CopyJobState.Paused or CopyJobState.Interrupted => "",         // Pause
        CopyJobState.Queued => "",            // Recent
        _ => "",                               // Copy
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

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
