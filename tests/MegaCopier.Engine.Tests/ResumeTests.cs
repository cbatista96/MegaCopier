namespace MegaCopier.Engine.Tests;

public sealed class ResumeTests : IDisposable
{
    // Tamaños pequeños para que las pruebas sean rápidas pero recorran muchos bloques.
    private const int Block = 64 * 1024;
    private const long Chunk = 256 * 1024;
    private const long FileLength = (3 * 1024 * 1024) + 1234; // 13 bloques, el último incompleto y sin alinear

    private readonly TempDir _temp = new();
    private readonly string _source;
    private readonly string _destinationDir;
    private readonly string _destination;

    public ResumeTests()
    {
        _source = _temp.Combine("origen.bin");
        _destinationDir = _temp.Combine("destino");
        _destination = Path.Combine(_destinationDir, "origen.bin");
        Directory.CreateDirectory(_destinationDir);
        TestFiles.Create(_source, FileLength, seed: 42);
    }

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Options(bool unbuffered = true, bool verify = false) => new()
    {
        BlockSize = Block,
        ChunkSize = Chunk,
        ReadQueueDepth = 3,
        WriteQueueDepth = 3,
        ResumableThreshold = 512 * 1024,
        UseUnbufferedIo = unbuffered,
        Verify = verify,
        RetryDelay = TimeSpan.Zero,
    };

    private CopyJobResult Run(CopyOptions options, IFaultInjector? faults = null)
    {
        var engine = new CopyEngine(options) { FaultInjector = faults };
        return engine.Run(new CopyJobRequest([_source], _destinationDir));
    }

    private void Crash(FaultPoint point, long chunk, CopyOptions? options = null) =>
        Assert.Throws<SimulatedPowerLoss>(() => Run(options ?? Options(), new CrashAt(point, chunk)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Copia_completa_identica(bool unbuffered)
    {
        var result = Run(Options(unbuffered));

        Assert.True(result.Success);
        Assert.Equal(FileLength, result.BytesTransferred);
        TestFiles.AssertSameContent(_source, _destination);
        Assert.False(File.Exists(_destination + ".mcpart"));
        Assert.False(File.Exists(_destination + ".mcjournal"));
        Assert.Equal(File.GetLastWriteTimeUtc(_source), File.GetLastWriteTimeUtc(_destination));
    }

    [Fact]
    public void Corte_tras_confirmar_un_bloque_continua_desde_el_siguiente()
    {
        Crash(FaultPoint.AfterJournalAppend, chunk: 5);

        Assert.False(File.Exists(_destination), "El archivo final no debe existir a medio copiar.");
        Assert.True(File.Exists(_destination + ".mcpart"));

        var result = Run(Options());

        Assert.Equal(6 * Chunk, result.BytesResumed);
        Assert.Equal(FileLength - (6 * Chunk), result.BytesTransferred);
        Assert.Equal(1, result.FilesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Corte_entre_vaciado_y_journal_no_cuenta_el_bloque_sin_anotar()
    {
        Crash(FaultPoint.AfterChunkFlush, chunk: 5);

        var result = Run(Options());

        Assert.Equal(5 * Chunk, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Corte_a_mitad_de_bloque_reanuda_desde_el_ultimo_confirmado()
    {
        Crash(FaultPoint.AfterBlockWrite, chunk: 7);

        var result = Run(Options());

        Assert.Equal(7 * Chunk, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Datos_perdidos_en_el_ultimo_bloque_se_detectan_y_se_recopian()
    {
        Crash(FaultPoint.AfterJournalAppend, chunk: 5);

        // El disco "mintió": el bloque 5 figura como confirmado pero contiene basura.
        using (var stream = new FileStream(_destination + ".mcpart", FileMode.Open, FileAccess.Write))
        {
            stream.Position = (5 * Chunk) + 100;
            stream.Write(new byte[500]);
        }

        var result = Run(Options());

        Assert.Equal(1, result.ChunksRejectedOnResume);
        Assert.Equal(5 * Chunk, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Basura_al_final_del_journal_se_ignora()
    {
        Crash(FaultPoint.AfterJournalAppend, chunk: 5);

        using (var stream = new FileStream(_destination + ".mcjournal", FileMode.Append))
        {
            stream.Write(new byte[20] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 });
        }

        var result = Run(Options());

        Assert.Equal(6 * Chunk, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Registro_roto_por_el_apagon_se_descarta()
    {
        Crash(FaultPoint.AfterJournalAppend, chunk: 5);

        // Rompe el CRC del último registro (el del bloque 5).
        using (var stream = new FileStream(_destination + ".mcjournal", FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = 512 + (5 * 32) + 30;
            stream.WriteByte(0xFF);
        }

        var result = Run(Options());

        Assert.Equal(5 * Chunk, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Si_el_origen_cambio_se_empieza_de_cero()
    {
        Crash(FaultPoint.AfterJournalAppend, chunk: 5);

        TestFiles.Create(_source, FileLength, seed: 7);
        File.SetLastWriteTimeUtc(_source, DateTime.UtcNow.AddMinutes(1));

        var result = Run(Options());

        Assert.Equal(0, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Cabecera_del_journal_danada_empieza_de_cero()
    {
        Crash(FaultPoint.AfterJournalAppend, chunk: 5);

        using (var stream = new FileStream(_destination + ".mcjournal", FileMode.Open, FileAccess.Write))
        {
            stream.Position = 10;
            stream.WriteByte(0x55);
        }

        var result = Run(Options());

        Assert.Equal(0, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Varios_cortes_seguidos_terminan_bien()
    {
        Crash(FaultPoint.AfterJournalAppend, chunk: 2);
        Crash(FaultPoint.AfterBlockWrite, chunk: 6);
        Crash(FaultPoint.AfterChunkFlush, chunk: 10);

        var result = Run(Options());

        Assert.Equal(10 * Chunk, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Verificacion_detecta_y_repara_un_bloque_danado()
    {
        var result = Run(Options(verify: true), new CorruptBeforeVerify(3 * Chunk));

        Assert.Equal(1, result.ChunksRepaired);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 8)]
    [InlineData(8, 1)]
    [InlineData(6, 6)]
    public void Cualquier_profundidad_de_cola_copia_y_reanuda_bien(int reads, int writes)
    {
        var options = Options();
        options.ReadQueueDepth = reads;
        options.WriteQueueDepth = writes;

        Crash(FaultPoint.AfterBlockWrite, chunk: 4, options);
        var result = Run(options);

        Assert.Equal(4 * Chunk, result.BytesResumed);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verificacion_repara_varios_bloques_danados(bool unbuffered)
    {
        var result = Run(Options(unbuffered, verify: true), new CorruptBeforeVerify(0, 2 * Chunk, 12 * Chunk));

        Assert.Equal(3, result.ChunksRepaired);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public async Task Pausar_detiene_la_copia_y_reanudar_la_termina()
    {
        var engine = new CopyEngine(Options());
        long committedWhilePaused = -1;
        using var paused = new ManualResetEventSlim();
        engine.ChunkCommitted += (_, bytes) =>
        {
            if (bytes == 2 * Chunk)
            {
                engine.Controller.Pause();
                paused.Set();
            }
        };

        var task = engine.RunAsync(new CopyJobRequest([_source], _destinationDir));
        Assert.True(paused.Wait(TimeSpan.FromSeconds(10)));

        // Durante la pausa no se confirma nada más.
        long before = new FileInfo(_destination + ".mcjournal").Length;
        await Task.Delay(300);
        committedWhilePaused = new FileInfo(_destination + ".mcjournal").Length - before;
        Assert.False(task.IsCompleted);

        engine.Controller.Resume();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, committedWhilePaused);
        Assert.True(result.Success);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Cancelar_conserva_lo_copiado_y_se_puede_continuar()
    {
        using var cancellation = new CancellationTokenSource();
        var engine = new CopyEngine(Options());
        engine.ChunkCommitted += (_, bytes) =>
        {
            if (bytes >= 4 * Chunk)
            {
                cancellation.Cancel();
            }
        };

        Assert.ThrowsAny<OperationCanceledException>(() => engine.Run(new CopyJobRequest([_source], _destinationDir), cancellation.Token));
        Assert.True(File.Exists(_destination + ".mcjournal"));

        var result = Run(Options());

        Assert.True(result.BytesResumed >= 4 * Chunk);
        TestFiles.AssertSameContent(_source, _destination);
    }

    [Fact]
    public void Archivo_pequeno_cortado_se_recopia_entero()
    {
        string small = _temp.Combine("pequeno.bin");
        TestFiles.Create(small, 300 * 1024, seed: 3);
        var options = Options();

        Assert.Throws<SimulatedPowerLoss>(() =>
            new CopyEngine(options) { FaultInjector = new CrashAt(FaultPoint.AfterBlockWrite, 0) }
                .Run(new CopyJobRequest([small], _destinationDir)));

        var target = Path.Combine(_destinationDir, "pequeno.bin");
        Assert.False(File.Exists(target));

        var result = new CopyEngine(options).Run(new CopyJobRequest([small], _destinationDir));

        Assert.Equal(0, result.BytesResumed);
        TestFiles.AssertSameContent(small, target);
        Assert.False(File.Exists(target + ".mcpart"));
    }

    [Fact]
    public void Archivo_vacio_se_copia()
    {
        string empty = _temp.Combine("vacio.txt");
        File.WriteAllBytes(empty, []);

        var result = new CopyEngine(Options()).Run(new CopyJobRequest([empty], _destinationDir));

        Assert.True(result.Success);
        Assert.Equal(0, new FileInfo(Path.Combine(_destinationDir, "vacio.txt")).Length);
    }
}
