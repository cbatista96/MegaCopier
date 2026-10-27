using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

public sealed class QueueTests : IDisposable
{
    private const long Chunk = 256 * 1024;

    private readonly TempDir _temp = new();
    private readonly JobStore _store;

    public QueueTests()
    {
        _store = new JobStore(_temp.Combine("jobs"));
    }

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Options() => new()
    {
        BlockSize = 64 * 1024,
        ChunkSize = Chunk,
        ResumableThreshold = 512 * 1024,
        RetryDelay = TimeSpan.Zero,
        ProgressInterval = TimeSpan.Zero, // cada bloque avisa: las pruebas pueden reaccionar a mitad de copia
    };

    private CopyQueue NewQueue() => new(_store, _ => Options());

    [Fact]
    public async Task Cancelar_carpeta_existente_avisa_del_final_sin_copiar_ni_dejar_trabajo()
    {
        string source = _temp.Combine("src");
        string destination = _temp.Combine("dst");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(destination, "src"));
        File.WriteAllText(Path.Combine(source, "archivo.txt"), "contenido nuevo");
        string existing = Path.Combine(destination, "src", "archivo.txt");
        File.WriteAllText(existing, "contenido original");
        var queue = new CopyQueue(_store, _ =>
        {
            var configured = Options();
            configured.FolderConflict = FolderConflictPolicy.Ask;
            return configured;
        });
        queue.FolderResolver = (_, _, _) => FolderDecision.Cancel;
        var finished = new TaskCompletionSource<CopyJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.JobFinished += job => finished.TrySetResult(job);
        var options = Options();
        options.FolderConflict = FolderConflictPolicy.Ask;

        var submitted = queue.Submit([source], destination, options);
        var ended = await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(submitted, ended);
        Assert.True(ended.Cancelled);
        Assert.Equal(CopyJobState.Interrupted, ended.State);
        Assert.Empty(queue.Jobs);
        Assert.Empty(_store.List());
        Assert.Equal("contenido original", File.ReadAllText(existing));
    }

    /// <summary>Se suscribe al trabajo en cuanto entra en la cola, antes de que empiece a copiar.</summary>
    private static void OnEveryChange(CopyQueue queue, Action<CopyJob> handler) =>
        queue.JobAdded += job => job.Changed += handler;

    private string NewSource(string name, long length, int seed)
    {
        string path = _temp.Combine(name);
        TestFiles.Create(path, length, seed);
        return path;
    }

    private static async Task<CopyJob> WaitFinished(CopyQueue queue, CopyJob job)
    {
        var done = new TaskCompletionSource<CopyJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.JobFinished += j =>
        {
            if (j == job)
            {
                done.TrySetResult(j);
            }
        };

        if (!job.IsActive)
        {
            return job;
        }

        return await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Un_trabajo_se_completa_y_se_borra_del_almacen()
    {
        var queue = NewQueue();
        string source = NewSource("a.bin", 2 * 1024 * 1024, 1);
        string destination = _temp.Combine("dst");

        var job = queue.Submit([source], destination, Options());
        Assert.Single(_store.List());

        await WaitFinished(queue, job);

        Assert.True(job.State == CopyJobState.Completed, $"{job.State}: {job.Error} {string.Join(" | ", job.Result?.Failures.Select(f => f.Message) ?? [])}");
        Assert.Empty(_store.List());
        TestFiles.AssertSameContent(source, Path.Combine(destination, "a.bin"));
    }

    [Fact]
    public async Task Dos_trabajos_en_el_mismo_disco_esperan_turno()
    {
        var queue = NewQueue();
        string first = NewSource("uno.bin", 4 * 1024 * 1024, 1);
        string second = NewSource("dos.bin", 1024, 2);

        var job1 = queue.Submit([first], _temp.Combine("d1"), Options());
        var job2 = queue.Submit([second], _temp.Combine("d2"), Options());

        // Ambos están en el disco temporal: el segundo espera mientras el primero sigue activo.
        Assert.True(job1.State is CopyJobState.Running or CopyJobState.Completed);
        if (job1.State == CopyJobState.Running)
        {
            Assert.Equal(CopyJobState.Queued, job2.State);
        }

        await WaitFinished(queue, job1);
        await WaitFinished(queue, job2);
        Assert.Equal(CopyJobState.Completed, job2.State);
    }

    [Fact]
    public async Task Detener_deja_el_trabajo_reanudable_y_reanudar_lo_termina()
    {
        var queue = NewQueue();
        string source = NewSource("grande.bin", 6 * 1024 * 1024, 3);
        string destination = _temp.Combine("dst");

        bool stopped = false;
        OnEveryChange(queue, j =>
        {
            if (!stopped && j.Progress is { CurrentFileBytes: >= 2 * Chunk } && j.State == CopyJobState.Running)
            {
                stopped = true;
                queue.Stop(j);
            }
        });
        var job = queue.Submit([source], destination, Options());

        await WaitFinished(queue, job);
        Assert.Equal(CopyJobState.Interrupted, job.State);
        Assert.Single(_store.List());

        queue.Resume(job);
        await WaitFinished(queue, job);

        Assert.Equal(CopyJobState.Completed, job.State);
        Assert.True(job.Result!.BytesResumed > 0);
        TestFiles.AssertSameContent(source, Path.Combine(destination, "grande.bin"));
    }

    [Fact]
    public async Task Trabajos_pendientes_se_cargan_como_interrumpidos_y_descartar_limpia()
    {
        string source = NewSource("grande.bin", 6 * 1024 * 1024, 4);
        string destination = _temp.Combine("dst");

        // Simula un apagón: la primera "sesión" muere con el trabajo a medias.
        Assert.Throws<SimulatedPowerLoss>(() =>
            new CopyEngine(Options()) { FaultInjector = new CrashAt(FaultPoint.AfterJournalAppend, 3) }
                .Run(new CopyJobRequest([source], destination)));
        _store.Save(CopyJobRecord.New([source], destination, Options()));

        var queue = NewQueue();
        var loaded = queue.LoadInterrupted();

        var job = Assert.Single(loaded);
        Assert.Equal(CopyJobState.Interrupted, job.State);
        Assert.True(File.Exists(Path.Combine(destination, "grande.bin.mcpart")));

        queue.Discard(job);

        Assert.Empty(queue.Jobs);
        Assert.Empty(_store.List());
        Assert.False(File.Exists(Path.Combine(destination, "grande.bin.mcpart")));
        Assert.False(File.Exists(Path.Combine(destination, "grande.bin.mcjournal")));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Pausar_y_reanudar_desde_la_cola()
    {
        var queue = NewQueue();
        string source = NewSource("grande.bin", 6 * 1024 * 1024, 5);
        bool paused = false;

        OnEveryChange(queue, j =>
        {
            if (!paused && j.State == CopyJobState.Running && j.Progress is { CurrentFileBytes: >= Chunk })
            {
                paused = true;
                queue.Pause(j);
                _ = Task.Delay(200).ContinueWith(_ => queue.Resume(j));
            }
        });
        var job = queue.Submit([source], _temp.Combine("dst"), Options());

        await WaitFinished(queue, job);

        Assert.True(paused);
        Assert.Equal(CopyJobState.Completed, job.State);
    }

    [Fact]
    public async Task Origen_inexistente_falla_y_no_queda_pendiente()
    {
        var queue = NewQueue();
        var job = queue.Submit([_temp.Combine("no-existe.bin")], _temp.Combine("dst"), Options());

        await WaitFinished(queue, job);

        Assert.Equal(CopyJobState.Failed, job.State);
        Assert.NotNull(job.Error);
        Assert.Empty(_store.List());
    }

    [Fact]
    public async Task Ipc_ida_y_vuelta()
    {
        string pipe = "MegaCopier-test-" + Guid.NewGuid().ToString("N");
        IpcRequest? received = null;
        await using var server = new IpcServer(
            request =>
            {
                received = request;
                return Task.FromResult(new IpcResponse { Ok = true, JobId = "abc" });
            },
            pipe);
        server.Start();

        var response = await IpcClient.TrySendAsync(
            new IpcRequest { Command = "copy", Sources = ["C:\\origen"], Destination = "D:\\destino", Verify = true, Conflict = ConflictPolicy.KeepBoth },
            connectTimeoutMs: 2000,
            pipeName: pipe);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.Equal("abc", response.JobId);
        Assert.NotNull(received);
        Assert.Equal("copy", received.Command);
        Assert.Equal(["C:\\origen"], received.Sources);
        Assert.Equal(ConflictPolicy.KeepBoth, received.Conflict);
    }

    [Fact]
    public async Task Ipc_sin_servidor_devuelve_null_rapido()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var response = await IpcClient.TrySendAsync(new IpcRequest(), connectTimeoutMs: 100, pipeName: "MegaCopier-nadie-" + Guid.NewGuid().ToString("N"));

        Assert.Null(response);
        Assert.True(watch.ElapsedMilliseconds < 5000); // margen para equipos lentos; sin servidor tarda unos 100 ms
    }

    [Fact]
    public void Al_cargar_la_misma_copia_repetida_solo_queda_una()
    {
        string source = NewSource("carpeta.bin", 1024, 5);
        string destination = _temp.Combine("dst");
        for (int i = 0; i < 5; i++)
        {
            var record = CopyJobRecord.New([source], destination, Options());
            record.CreatedUtc = DateTime.UtcNow.AddMinutes(i);
            _store.Save(record);
        }

        _store.Save(CopyJobRecord.New([source], _temp.Combine("otro"), Options())); // otra copia distinta

        var loaded = NewQueue().LoadInterrupted();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(2, _store.List().Count);
    }

    [Fact]
    public async Task Detener_por_el_usuario_cancela_y_la_quita_de_la_lista()
    {
        var queue = NewQueue();
        string source = NewSource("grande.bin", 6 * 1024 * 1024, 6);
        bool stopped = false;
        OnEveryChange(queue, j =>
        {
            if (!stopped && j.Progress is { CurrentFileBytes: >= 2 * Chunk } && j.State == CopyJobState.Running)
            {
                stopped = true;
                queue.Cancel(j);
            }
        });
        bool removed = false;
        queue.JobRemoved += _ => removed = true;
        var job = queue.Submit([source], _temp.Combine("dst"), Options());

        await WaitFinished(queue, job);

        Assert.True(removed);
        Assert.Empty(queue.Jobs);
        Assert.Empty(_store.List());
    }

    [Fact]
    public async Task Detener_una_copia_en_espera_la_quita_sin_empezarla()
    {
        var queue = NewQueue();
        queue.MaxActiveJobs = 1;
        var first = queue.Submit([NewSource("uno.bin", 6 * 1024 * 1024, 7)], _temp.Combine("d1"), Options());
        var second = queue.Submit([NewSource("dos.bin", 1024, 8)], _temp.Combine("d2"), Options());
        Assert.Equal(CopyJobState.Queued, second.State);

        queue.Cancel(second);

        Assert.DoesNotContain(second, queue.Jobs);
        await WaitFinished(queue, first);
        Assert.False(File.Exists(_temp.Combine("d2", "dos.bin")));
        Assert.Empty(_store.List());
    }

    [Fact]
    public async Task Continuar_una_copia_en_espera_la_empieza_sin_esperar_turno()
    {
        var queue = NewQueue();
        queue.MaxActiveJobs = 1;
        bool paused = false;
        OnEveryChange(queue, j =>
        {
            if (!paused && j.Progress is { CurrentFileBytes: >= Chunk } && j.State == CopyJobState.Running)
            {
                paused = true;
                queue.Pause(j);
            }
        });
        var first = queue.Submit([NewSource("uno.bin", 6 * 1024 * 1024, 9)], _temp.Combine("d1"), Options());
        var second = queue.Submit([NewSource("dos.bin", 1024, 10)], _temp.Combine("d2"), Options());
        Assert.Equal(CopyJobState.Queued, second.State);

        queue.Resume(second); // la primera sigue (en marcha o en pausa) y la segunda empieza igualmente

        await WaitFinished(queue, second);
        Assert.Equal(CopyJobState.Completed, second.State);
        Assert.True(first.IsActive);
        while (first.State == CopyJobState.Running) // en equipos lentos, la pausa puede llegar después
        {
            await Task.Delay(10);
        }

        queue.Resume(first);
        await WaitFinished(queue, first);
        Assert.Equal(CopyJobState.Completed, first.State);
    }

    [Fact]
    public void Sin_espacio_se_pregunta_y_cancelar_no_copia_nada()
    {
        string source = NewSource("a.bin", 1024 * 1024, 11);
        var engine = new CopyEngine(Options()) { FreeBytes = _ => 1000 };
        SpaceShortage? asked = null;
        engine.SpaceResolver = (shortage, _) =>
        {
            asked = shortage;
            return false;
        };

        Assert.Throws<SpaceCancelledException>(() => engine.Run(new CopyJobRequest([source], _temp.Combine("dst"))));
        Assert.Equal(1024 * 1024, asked!.Needed);
        Assert.Equal(1000, asked.Free);
        Assert.False(File.Exists(_temp.Combine("dst", "a.bin")));
    }

    [Fact]
    public void Sin_espacio_forzar_copia_igualmente()
    {
        string source = NewSource("a.bin", 1024 * 1024, 12);
        var engine = new CopyEngine(Options()) { FreeBytes = _ => 1000, SpaceResolver = (_, _) => true };

        var result = engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.True(engine.SpaceForced);
        TestFiles.AssertSameContent(source, _temp.Combine("dst", "a.bin"));
    }

    [Fact]
    public async Task Al_anadir_a_una_copia_en_marcha_se_comprueba_si_cabe()
    {
        var queue = NewQueue();
        bool paused = false;
        OnEveryChange(queue, j =>
        {
            if (!paused && j.Progress is { CurrentFileBytes: >= Chunk } && j.State == CopyJobState.Running)
            {
                paused = true;
                queue.Pause(j);
            }
        });
        var job = queue.Submit([NewSource("uno.bin", 6 * 1024 * 1024, 13)], _temp.Combine("dst"), Options());
        while (job.State != CopyJobState.Paused)
        {
            await Task.Delay(10);
        }

        string extra = NewSource("extra.bin", 1024 * 1024, 14);
        Assert.Null(job.CheckSpaceFor([extra])); // en el disco de pruebas hay sitio de sobra

        job.ForceSpace();
        Assert.True(job.Record.SpaceForced);
        Assert.Equal(1, job.AddSources([extra]));
        queue.Resume(job);
        await WaitFinished(queue, job);
        TestFiles.AssertSameContent(extra, _temp.Combine("dst", "extra.bin"));
    }

    [Fact]
    public void Volver_a_copiar_lo_mismo_encuentra_la_copia_detenida()
    {
        string source = NewSource("carpeta.bin", 1024, 15);
        string destination = _temp.Combine("dst");
        _store.Save(CopyJobRecord.New([source], destination, Options()));
        var queue = NewQueue();
        var loaded = queue.LoadInterrupted();

        Assert.Same(loaded[0], queue.FindInterrupted([source.ToUpperInvariant() + "\\"], destination, JobOperation.Copy, move: false));
        Assert.Null(queue.FindInterrupted([source], _temp.Combine("otro"), JobOperation.Copy, move: false));
        Assert.Null(queue.FindInterrupted([source], destination, JobOperation.Copy, move: true));
    }

    [Fact]
    public async Task Una_copia_nueva_que_empieza_ya_no_espera_a_la_otra()
    {
        var queue = NewQueue();
        queue.MaxActiveJobs = 1;
        bool paused = false;
        OnEveryChange(queue, j =>
        {
            if (!paused && j.Progress is { CurrentFileBytes: >= Chunk } && j.State == CopyJobState.Running)
            {
                paused = true;
                queue.Pause(j);
            }
        });
        var first = queue.Submit([NewSource("uno.bin", 6 * 1024 * 1024, 16)], _temp.Combine("d1"), Options());
        var second = queue.Submit([NewSource("dos.bin", 1024, 17)], _temp.Combine("d2"), Options(), startNow: true);

        await WaitFinished(queue, second);
        Assert.Equal(CopyJobState.Completed, second.State);
        while (first.State == CopyJobState.Running)
        {
            await Task.Delay(10);
        }

        queue.Resume(first);
        await WaitFinished(queue, first);
        Assert.Equal(CopyJobState.Completed, first.State);
    }

    [Fact]
    public async Task Anadir_a_una_copia_hacia_otra_carpeta_del_disco_y_reanudar()
    {
        var queue = NewQueue();
        bool paused = false;
        OnEveryChange(queue, j =>
        {
            if (!paused && j.Progress is { CurrentFileBytes: >= Chunk } && j.State == CopyJobState.Running)
            {
                paused = true;
                queue.Pause(j);
            }
        });
        var job = queue.Submit([NewSource("uno.bin", 6 * 1024 * 1024, 18)], _temp.Combine("dst"), Options());
        while (job.State != CopyJobState.Paused)
        {
            await Task.Delay(10);
        }

        // En marcha: lo nuevo va a su propia carpeta y queda apuntado para reanudar.
        string extra = NewSource("extra.bin", 1024 * 1024, 19);
        Assert.Null(job.CheckSpaceFor([extra], _temp.Combine("otra")));
        Assert.Same(job, queue.AddToJob(job, [extra], destination: _temp.Combine("otra")));
        Assert.Equal(_temp.Combine("otra"), job.Record.ExtraTargets[extra]);
        Assert.DoesNotContain(extra, job.Record.Sources);

        // Se detiene (como al cerrar MegaCopier) y al reanudar copia también lo añadido, en su carpeta.
        queue.Stop(job);
        await WaitFinished(queue, job);
        Assert.Equal(CopyJobState.Interrupted, job.State);
        queue.Resume(job);
        await WaitFinished(queue, job);
        Assert.Equal(CopyJobState.Completed, job.State);
        TestFiles.AssertSameContent(extra, _temp.Combine("otra", "extra.bin"));
        Assert.False(File.Exists(_temp.Combine("dst", "extra.bin")));

        // En espera: se suma a sus orígenes con su carpeta.
        var queued = queue.Submit([NewSource("tres.bin", 1024, 20)], _temp.Combine("d3"), Options(), startAtUtc: DateTime.UtcNow.AddHours(1));
        string more = NewSource("mas.bin", 1024, 21);
        queue.AddToJob(queued, [more], destination: _temp.Combine("d4"));
        Assert.Equal(_temp.Combine("d4"), queued.Record.ExtraTargets[more]);
        queue.Unschedule(queued);
        await WaitFinished(queue, queued);
        TestFiles.AssertSameContent(more, _temp.Combine("d4", "mas.bin"));
    }
}
