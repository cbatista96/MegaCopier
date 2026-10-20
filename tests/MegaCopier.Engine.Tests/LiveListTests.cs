using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace MegaCopier.Engine.Tests;

public sealed class LiveListTests : IDisposable
{
    private const long Chunk = 256 * 1024;

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Options() => new()
    {
        BlockSize = 64 * 1024,
        ChunkSize = Chunk,
        ResumableThreshold = 512 * 1024,
        RetryDelay = TimeSpan.Zero,
    };

    private string NewFile(string name, long length, int seed)
    {
        string path = _temp.Combine(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestFiles.Create(path, length, seed);
        return path;
    }

    private static string Sha256Of(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    [Theory]
    [InlineData(FileHashAlgorithm.Crc32, "352441c2")]
    [InlineData(FileHashAlgorithm.XxHash64, "44bc2cf5ad770999")]
    [InlineData(FileHashAlgorithm.XxHash3, "78af5f94892f3950")]
    [InlineData(FileHashAlgorithm.Md5, "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData(FileHashAlgorithm.Sha1, "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData(FileHashAlgorithm.Sha256, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Hashes_coinciden_con_los_valores_de_referencia(FileHashAlgorithm algorithm, string expected)
    {
        var hasher = FileHasher.Create(algorithm)!;
        hasher.Append("a"u8);
        hasher.Append("bc"u8); // en dos trozos: el hash es incremental

        Assert.Equal(expected, hasher.Finish());
    }

    [Fact]
    public void XxHash128_coincide_con_el_calculo_de_una_vez()
    {
        var hasher = FileHasher.Create(FileHashAlgorithm.XxHash128)!;
        hasher.Append("abc"u8);

        Assert.Equal(XxHash128.HashToUInt128("abc"u8).ToString("x32"), hasher.Finish());
    }

    [Fact]
    public void Copia_con_verificacion_deja_ambos_hashes_y_estado_verificado()
    {
        string source = NewFile("datos.bin", 3 * 1024 * 1024 + 17, 1);
        var options = Options();
        options.HashAlgorithm = FileHashAlgorithm.Sha256;
        options.Verify = true;
        var engine = new CopyEngine(options);

        engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        var item = Assert.Single(engine.Files.Snapshot());
        Assert.Equal(FileState.Verified, item.State);
        Assert.Equal(Sha256Of(source), item.SourceHash);
        Assert.Equal(item.SourceHash, item.DestinationHash);
    }

    [Fact]
    public void Al_reanudar_el_hash_cubre_el_archivo_entero()
    {
        string source = NewFile("grande.bin", 4 * 1024 * 1024 + 99, 2);
        string destination = _temp.Combine("dst");
        var options = Options();
        options.HashAlgorithm = FileHashAlgorithm.Sha256;
        options.SaveChecksumFile = true; // algo usa el hash: si nada lo usa, al reanudar no se calcula

        Assert.Throws<SimulatedPowerLoss>(() =>
            new CopyEngine(options) { FaultInjector = new CrashAt(FaultPoint.AfterJournalAppend, 5) }
                .Run(new CopyJobRequest([source], destination)));

        var engine = new CopyEngine(options);
        var result = engine.Run(new CopyJobRequest([source], destination));

        Assert.True(result.BytesResumed > 0);
        Assert.Equal(Sha256Of(source), Assert.Single(engine.Files.Snapshot()).SourceHash);
    }

    [Fact]
    public void Lista_reordena_y_quita_solo_pendientes()
    {
        var list = new TransferList();
        var a = list.Add("a", "A", 1, default);
        var b = list.Add("b", "B", 1, default);
        var c = list.Add("c", "C", 1, default);
        var d = list.Add("d", "D", 1, default);

        Assert.Same(a, list.TakeNext()); // a está en curso
        Assert.True(list.MoveToTop(d.Id));
        Assert.Equal(["a", "d", "b", "c"], list.Snapshot().Select(i => i.Source));
        Assert.False(list.MoveUp(d.Id)); // nunca por delante de lo empezado
        Assert.True(list.MoveToBottom(d.Id));
        Assert.True(list.MoveUp(c.Id));
        Assert.Equal(["a", "c", "b", "d"], list.Snapshot().Select(i => i.Source));
        Assert.False(list.Remove(a.Id)); // el que está en curso no se quita
        Assert.True(list.Remove(b.Id));
        Assert.Same(c, list.TakeNext());
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Quitar_un_archivo_de_la_lista_hace_que_no_se_copie()
    {
        string folder = _temp.Combine("src");
        NewFile(Path.Combine("src", "uno.bin"), 1000, 1);
        NewFile(Path.Combine("src", "dos.bin"), 1000, 2);
        var engine = new CopyEngine(Options());
        engine.Files.Changed += change =>
        {
            if (change.Kind == TransferListChangeKind.Added && change.Item.Source.EndsWith("dos.bin", StringComparison.Ordinal))
            {
                engine.Files.Remove(change.Item.Id);
            }
        };

        engine.Run(new CopyJobRequest([folder], _temp.Combine("dst")));

        Assert.True(File.Exists(_temp.Combine("dst", "src", "uno.bin")));
        Assert.False(File.Exists(_temp.Combine("dst", "src", "dos.bin")));
    }

    [Fact]
    public void Saltar_el_archivo_actual_lo_descarta_y_sigue_con_los_demas()
    {
        string folder = _temp.Combine("src");
        NewFile(Path.Combine("src", "a-grande.bin"), 6 * 1024 * 1024, 1);
        NewFile(Path.Combine("src", "b-pequeno.bin"), 1000, 2);
        var options = Options();
        bool skipped = false;
        var engine = new CopyEngine(options);
        engine.ChunkCommitted += (path, bytes) =>
        {
            if (!skipped && path.EndsWith("a-grande.bin", StringComparison.Ordinal) && bytes >= 2 * Chunk)
            {
                skipped = true;
                engine.SkipCurrent();
            }
        };

        var result = engine.Run(new CopyJobRequest([folder], _temp.Combine("dst")));

        var big = engine.Files.Snapshot().Single(i => i.Source.EndsWith("a-grande.bin", StringComparison.Ordinal));
        Assert.Equal(FileState.Skipped, big.State);
        Assert.False(File.Exists(big.Destination));
        Assert.False(File.Exists(big.Destination + ".mcpart"));
        Assert.False(File.Exists(big.Destination + ".mcjournal"));
        Assert.True(File.Exists(_temp.Combine("dst", "src", "b-pequeno.bin")));
        Assert.Equal(1, result.FilesSkipped);
    }

    [Fact]
    public async Task Saltar_en_pausa_descarta_el_archivo_y_la_copia_sigue_en_pausa()
    {
        string folder = _temp.Combine("src");
        NewFile(Path.Combine("src", "a-grande.bin"), 6 * 1024 * 1024, 1);
        NewFile(Path.Combine("src", "b-siguiente.bin"), 4 * 1024 * 1024, 2);
        bool paused = false;
        var engine = new CopyEngine(Options());
        engine.ChunkCommitted += (path, bytes) =>
        {
            if (!paused && path.EndsWith("a-grande.bin", StringComparison.Ordinal) && bytes >= 2 * Chunk)
            {
                paused = true;
                engine.Controller.Pause();
            }
        };

        var run = engine.RunAsync(new CopyJobRequest([folder], _temp.Combine("dst")));
        Assert.True(SpinWait.SpinUntil(() => paused, TimeSpan.FromSeconds(20)));
        engine.SkipCurrent();

        // Sigue en pausa: el siguiente archivo espera sin copiarse.
        string small = _temp.Combine("dst", "src", "b-siguiente.bin");
        Assert.True(SpinWait.SpinUntil(
            () => engine.Files.Snapshot().Any(i => i.Source.EndsWith("a-grande.bin", StringComparison.Ordinal) && i.State == FileState.Skipped),
            TimeSpan.FromSeconds(20)));
        Thread.Sleep(300);
        Assert.False(run.IsCompleted);
        Assert.False(File.Exists(small));
        Assert.True(engine.Controller.IsPaused);

        engine.Controller.Resume();
        var result = await run;

        Assert.True(File.Exists(small));
        Assert.Equal(1, result.FilesSkipped);
    }

    [Fact]
    public void Anadir_origenes_durante_la_copia()
    {
        string first = NewFile("primero.bin", 4 * 1024 * 1024, 1);
        string extra = NewFile("extra.bin", 5000, 2);
        bool added = false;
        var engine = new CopyEngine(Options());
        engine.ChunkCommitted += (_, _) =>
        {
            if (!added)
            {
                added = true;
                engine.AddSources([extra]);
            }
        };

        engine.Run(new CopyJobRequest([first], _temp.Combine("dst")));

        Assert.True(added);
        TestFiles.AssertSameContent(extra, _temp.Combine("dst", "extra.bin"));
        Assert.Equal(2, engine.Files.Count);
    }

    [Fact]
    public void Guarda_el_archivo_de_sumas_en_formato_estandar()
    {
        string folder = _temp.Combine("src");
        NewFile(Path.Combine("src", "a.bin"), 1234, 1);
        NewFile(Path.Combine("src", "sub", "b.bin"), 4321, 2);
        var options = Options();
        options.HashAlgorithm = FileHashAlgorithm.Sha256;
        options.SaveChecksumFile = true;
        var engine = new CopyEngine(options);

        engine.Run(new CopyJobRequest([folder], _temp.Combine("dst")));

        Assert.NotNull(engine.ChecksumFile);
        Assert.EndsWith(".sha256", engine.ChecksumFile);
        var lines = File.ReadAllLines(engine.ChecksumFile!);
        Assert.Contains($"{Sha256Of(_temp.Combine("src", "a.bin"))} *{Path.Combine("src", "a.bin")}", lines);
        Assert.Contains($"{Sha256Of(_temp.Combine("src", "sub", "b.bin"))} *{Path.Combine("src", "sub", "b.bin")}", lines);
    }

    [Fact]
    public void Copia_los_streams_alternativos()
    {
        string source = NewFile("con-stream.txt", 100, 1);
        File.WriteAllText(source + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");
        File.WriteAllText(source + ":notas", "contenido oculto");

        new CopyEngine(Options()).Run(new CopyJobRequest([source], _temp.Combine("dst")));

        string copy = _temp.Combine("dst", "con-stream.txt");
        Assert.Equal("contenido oculto", File.ReadAllText(copy + ":notas"));
        Assert.Contains("ZoneId=3", File.ReadAllText(copy + ":Zone.Identifier"));
    }

    [Fact]
    public void Verificar_omitidos_detecta_un_identico_que_no_lo_es()
    {
        string source = NewFile("foto.jpg", 2000, 1);
        string destination = _temp.Combine("dst");
        Directory.CreateDirectory(destination);
        string existing = Path.Combine(destination, "foto.jpg");
        TestFiles.Create(existing, 2000, 99); // mismo tamaño, otro contenido
        File.SetLastWriteTimeUtc(existing, File.GetLastWriteTimeUtc(source)); // misma fecha

        var options = Options();
        options.VerifySkipped = true;
        var engine = new CopyEngine(options);
        engine.Run(new CopyJobRequest([source], destination));

        var item = Assert.Single(engine.Files.Snapshot());
        Assert.Equal(FileState.Failed, item.State);
        Assert.NotEqual(item.SourceHash, item.DestinationHash);
    }

    [Fact]
    public void Desatendido_no_pregunta_y_omite_los_conflictos()
    {
        string source = _temp.Combine("a.txt");
        string destination = _temp.Combine("dst");
        Directory.CreateDirectory(destination);
        File.WriteAllText(source, "nuevo contenido");
        File.WriteAllText(Path.Combine(destination, "a.txt"), "viejo");

        var options = Options();
        options.Conflict = ConflictPolicy.Ask;
        options.Unattended = true;
        var engine = new CopyEngine(options) { ConflictResolver = (_, _) => throw new InvalidOperationException("No debía preguntar") };

        var result = engine.Run(new CopyJobRequest([source], destination));

        Assert.Equal(1, result.FilesSkipped);
        Assert.Equal("viejo", File.ReadAllText(Path.Combine(destination, "a.txt")));
    }

    [Fact]
    public void Registro_y_estadisticas()
    {
        string folder = _temp.Combine("src");
        NewFile(Path.Combine("src", "a.bin"), 1000, 1);
        NewFile(Path.Combine("src", "b.bin"), 3000, 2);
        var log = new List<LogEntry>();
        var engine = new CopyEngine(Options());
        engine.Logged += log.Add;

        engine.Run(new CopyJobRequest([folder], _temp.Combine("dst")));

        Assert.Contains(log, e => e.Message.StartsWith("Copiar 2 archivos", StringComparison.Ordinal));
        Assert.Contains(log, e => e.Message.StartsWith("Terminado", StringComparison.Ordinal));
        var stats = TransferStatistics.From(engine.Files.Snapshot());
        Assert.Equal(2, stats.CopiedFiles);
        Assert.Equal(4000, stats.CopiedBytes);
        Assert.Equal(0, stats.PendingFiles);
    }
}
