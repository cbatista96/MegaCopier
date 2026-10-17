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
}
