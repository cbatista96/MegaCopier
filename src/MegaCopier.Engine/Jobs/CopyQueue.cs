using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Jobs;

public enum CopyJobState
{
    /// <summary>Esperando a que quede libre el disco.</summary>
    Queued,
    Running,
    Paused,
    Completed,

    /// <summary>Terminó, pero algunos archivos fallaron.</summary>
    CompletedWithErrors,

    /// <summary>Error que impidió continuar (origen inexistente, sin espacio...).</summary>
    Failed,

    /// <summary>Detenida (por el usuario, un apagón o un cierre): se puede reanudar desde donde quedó.</summary>
    Interrupted,
}

/// <summary>Un trabajo de la cola, observable desde la interfaz.</summary>
public sealed class CopyJob
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _cancellation;
    private CopyEngine? _engine;

    internal CopyJob(CopyJobRecord record, CopyJobState state)
    {
        Record = record;
        State = state;
        Devices = record.Sources.Append(record.Destination).Select(VolumeInfo.DeviceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string Id => Record.Id;

    public CopyJobRecord Record { get; }

    public CopyJobState State { get; private set; }

    public CopyProgress? Progress { get; private set; }

    public CopyJobResult? Result { get; private set; }

    public string? Error { get; private set; }

    internal IReadOnlySet<string> Devices { get; }

    public bool IsActive => State is CopyJobState.Queued or CopyJobState.Running or CopyJobState.Paused;

    /// <summary>Cambió el estado o el progreso. Se dispara desde hilos de trabajo.</summary>
    public event Action<CopyJob>? Changed;

    internal void SetState(CopyJobState state, string? error = null)
    {
        lock (_lock)
        {
            State = state;
            if (error is not null || state is CopyJobState.Queued or CopyJobState.Running)
            {
                Error = error;
            }
        }

        Changed?.Invoke(this);
    }

    internal void SetProgress(CopyProgress progress)
    {
        Progress = progress;
        Changed?.Invoke(this);
    }

    internal void SetResult(CopyJobResult result) => Result = result;

    internal void Attach(CopyEngine engine, CancellationTokenSource cancellation)
    {
        lock (_lock)
        {
            _engine = engine;
            _cancellation = cancellation;
        }
    }

    internal void Detach()
    {
        lock (_lock)
        {
            _engine = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    internal bool Pause()
    {
        lock (_lock)
        {
            if (_engine is null || State != CopyJobState.Running)
            {
                return false;
            }

            _engine.Controller.Pause();
        }

        SetState(CopyJobState.Paused);
        return true;
    }

    internal bool Unpause()
    {
        lock (_lock)
        {
            if (_engine is null || State != CopyJobState.Paused)
            {
                return false;
            }

            _engine.Controller.Resume();
        }

        SetState(CopyJobState.Running);
        return true;
    }

    internal void RequestCancel()
    {
        lock (_lock)
        {
            _cancellation?.Cancel();
            _engine?.Controller.Resume(); // si estaba en pausa, que despierte para ver la cancelación
        }
    }
}

/// <summary>
/// Cola de trabajos del proceso residente. Cada trabajo se guarda en <see cref="JobStore"/> antes de empezar y se
/// borra al terminar bien, así que tras un apagón la cola sabe exactamente qué quedó a medias.
/// Los trabajos que comparten disco físico se ejecutan de uno en uno; en discos distintos, en paralelo.
/// </summary>
public sealed class CopyQueue
{
    private readonly Lock _lock = new();
    private readonly List<CopyJob> _jobs = [];
    private readonly JobStore _store;
    private readonly Func<CopyJobRecord, CopyOptions> _optionsFor;

    public CopyQueue(JobStore store, Func<CopyJobRecord, CopyOptions>? optionsFor = null)
    {
        _store = store;
        _optionsFor = optionsFor ?? (record => record.ToOptions());
    }

    public event Action<CopyJob>? JobAdded;

    public event Action<CopyJob>? JobRemoved;

    /// <summary>Se dispara cuando un trabajo termina (bien, con errores, fallido o interrumpido).</summary>
    public event Action<CopyJob>? JobFinished;

    public IReadOnlyList<CopyJob> Jobs
    {
        get
        {
            lock (_lock)
            {
                return [.. _jobs];
            }
        }
    }

    public bool HasActiveJobs
    {
        get
        {
            lock (_lock)
            {
                return _jobs.Any(j => j.IsActive);
            }
        }
    }

    /// <summary>Carga los trabajos que quedaron pendientes (cierre, apagón...) como interrumpidos.</summary>
    public IReadOnlyList<CopyJob> LoadInterrupted()
    {
        var loaded = new List<CopyJob>();
        foreach (var record in _store.List())
        {
            lock (_lock)
            {
                if (_jobs.Any(j => j.Id == record.Id))
                {
                    continue;
                }
            }

            loaded.Add(Add(new CopyJob(record, CopyJobState.Interrupted)));
        }

        return loaded;
    }

    public CopyJob Submit(IEnumerable<string> sources, string destination, CopyOptions options)
    {
        var record = CopyJobRecord.New(sources, destination, options);
        _store.Save(record);
        var job = Add(new CopyJob(record, CopyJobState.Queued));
        Schedule();
        return job;
    }

    public void Resume(CopyJob job)
    {
        if (job.State is CopyJobState.Paused)
        {
            job.Unpause();
            return;
        }

        if (job.State is CopyJobState.Interrupted or CopyJobState.Failed or CopyJobState.CompletedWithErrors)
        {
            _store.Save(job.Record);
            job.SetState(CopyJobState.Queued);
            Schedule();
        }
    }

    public void Pause(CopyJob job) => job.Pause();

    /// <summary>Detiene el trabajo conservando lo copiado: se puede reanudar más tarde.</summary>
    public void Stop(CopyJob job)
    {
        if (job.State == CopyJobState.Queued)
        {
            job.SetState(CopyJobState.Interrupted);
            return;
        }

        job.RequestCancel();
    }

    /// <summary>Olvida el trabajo y borra los archivos a medias (.mcpart / .mcjournal) que dejó.</summary>
    public void Discard(CopyJob job)
    {
        if (job.IsActive)
        {
            throw new InvalidOperationException("Detén el trabajo antes de descartarlo.");
        }

        _store.Delete(job.Id);
        CopyEngine.DeletePartialFiles(new CopyJobRequest(job.Record.Sources, job.Record.Destination));
        Remove(job);
    }

    /// <summary>Quita de la lista un trabajo terminado.</summary>
    public void Clear(CopyJob job)
    {
        if (job.State is CopyJobState.Completed)
        {
            Remove(job);
        }
    }

    public void PauseAll()
    {
        foreach (var job in Jobs)
        {
            job.Pause();
        }
    }

    public void ResumeAll()
    {
        foreach (var job in Jobs.Where(j => j.State == CopyJobState.Paused))
        {
            job.Unpause();
        }
    }

    /// <summary>Detiene todo (para salir de la aplicación) y espera a que los trabajos suelten los archivos.</summary>
    public void StopAll(TimeSpan timeout)
    {
        foreach (var job in Jobs.Where(j => j.IsActive))
        {
            Stop(job);
        }

        var deadline = DateTime.UtcNow + timeout;
        while (Jobs.Any(j => j.State is CopyJobState.Running or CopyJobState.Paused) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }
    }

    private CopyJob Add(CopyJob job)
    {
        lock (_lock)
        {
            _jobs.Add(job);
        }

        JobAdded?.Invoke(job);
        return job;
    }

    private void Remove(CopyJob job)
    {
        bool removed;
        lock (_lock)
        {
            removed = _jobs.Remove(job);
        }

        if (removed)
        {
            JobRemoved?.Invoke(job);
        }
    }

    private void Schedule()
    {
        var toStart = new List<CopyJob>();
        lock (_lock)
        {
            var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var running in _jobs.Where(j => j.State is CopyJobState.Running or CopyJobState.Paused))
            {
                busy.UnionWith(running.Devices);
            }

            foreach (var job in _jobs.Where(j => j.State == CopyJobState.Queued))
            {
                if (job.Devices.Overlaps(busy))
                {
                    continue;
                }

                busy.UnionWith(job.Devices);
                job.SetState(CopyJobState.Running);
                toStart.Add(job);
            }
        }

        foreach (var job in toStart)
        {
            Start(job);
        }
    }

    private void Start(CopyJob job)
    {
        var engine = new CopyEngine(_optionsFor(job.Record));
        var cancellation = new CancellationTokenSource();
        job.Attach(engine, cancellation);
        engine.ProgressChanged += job.SetProgress;

        _ = engine.RunAsync(new CopyJobRequest(job.Record.Sources, job.Record.Destination), cancellation.Token)
            .ContinueWith(
                task =>
                {
                    job.Detach();
                    if (task.IsCanceled || task.Exception?.InnerException is OperationCanceledException)
                    {
                        job.SetState(CopyJobState.Interrupted);
                    }
                    else if (task.Exception?.InnerException is { } error)
                    {
                        // Un error de plan no se arregla reintentando; el resto queda reanudable.
                        if (error is CopyPlanException)
                        {
                            _store.Delete(job.Id);
                        }

                        job.SetState(CopyJobState.Failed, error.Message);
                    }
                    else
                    {
                        var result = task.Result;
                        job.SetResult(result);
                        if (result.Success)
                        {
                            _store.Delete(job.Id);
                            job.SetState(CopyJobState.Completed);
                        }
                        else
                        {
                            job.SetState(CopyJobState.CompletedWithErrors, $"{result.Failures.Count} archivo(s) con errores");
                        }
                    }

                    JobFinished?.Invoke(job);
                    Schedule();
                },
                TaskScheduler.Default);
    }
}
