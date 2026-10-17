using System.Diagnostics;

namespace MegaCopier.Engine.Tests;

public sealed class MoveAndConflictTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Options(bool move = false) => new()
    {
        BlockSize = 64 * 1024,
        ChunkSize = 256 * 1024,
        ResumableThreshold = 512 * 1024,
        RetryDelay = TimeSpan.Zero,
        Move = move,
    };

    private string Tree(string name)
    {
        string root = _temp.Combine(name);
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        Directory.CreateDirectory(Path.Combine(root, "vacia"));
        TestFiles.Create(Path.Combine(root, "a.bin"), 3 * 1024 * 1024 + 5, 1);
        TestFiles.Create(Path.Combine(root, "sub", "b.bin"), 1000, 2);
        return root;
    }

    private static Dictionary<string, byte[]> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllBytes);

    private static void AssertSameTree(Dictionary<string, byte[]> expected, string root)
    {
        var actual = Snapshot(root);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, bytes) in expected)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(actual[path]), $"{path} no coincide");
        }
    }

    [Fact]
    public void Mover_en_el_mismo_volumen_renombra_al_instante()
    {
        string source = Tree("Origen");
        var before = Snapshot(source);
        string destination = _temp.Combine("destino");

        var watch = Stopwatch.StartNew();
        var result = new CopyEngine(Options(move: true)).Run(new CopyJobRequest([source], destination));

        Assert.True(result.Success);
        Assert.False(Directory.Exists(source));
        Assert.Equal(0, result.BytesTransferred); // ni un byte copiado: fue un renombrado
        AssertSameTree(before, Path.Combine(destination, "Origen"));
        Assert.True(Directory.Exists(Path.Combine(destination, "Origen", "vacia")));
        Assert.True(watch.ElapsedMilliseconds < 10000); // margen para equipos lentos: el renombrado ya lo prueba BytesTransferred
    }

    [Fact]
    public void Mover_entre_volumenes_copia_y_borra_el_origen()
    {
        string source = Tree("Origen");
        var before = Snapshot(source);
        string destination = _temp.Combine("destino");

        var engine = new CopyEngine(Options(move: true)) { SameVolumeOverride = (_, _) => false };
        var result = engine.Run(new CopyJobRequest([source], destination));

        Assert.True(result.Success);
        Assert.True(result.BytesTransferred > 0);
        Assert.False(Directory.Exists(source), "Las carpetas de origen vacías deben desaparecer.");
        AssertSameTree(before, Path.Combine(destination, "Origen"));
    }

    [Fact]
    public void Mover_entre_volumenes_cortado_se_reanuda_y_no_pierde_nada()
    {
        string source = Tree("Origen");
        var before = Snapshot(source);
        string destination = _temp.Combine("destino");

        Assert.Throws<SimulatedPowerLoss>(() =>
            new CopyEngine(Options(move: true)) { SameVolumeOverride = (_, _) => false, FaultInjector = new CrashAt(FaultPoint.AfterJournalAppend, 4) }
                .Run(new CopyJobRequest([source], destination)));

        // Nada se borró del origen mientras su copia no estaba completa.
        Assert.True(File.Exists(Path.Combine(source, "a.bin")));

        var result = new CopyEngine(Options(move: true)) { SameVolumeOverride = (_, _) => false }
            .Run(new CopyJobRequest([source], destination));

        Assert.True(result.Success);
        Assert.True(result.BytesResumed > 0);
        Assert.False(Directory.Exists(source));
        AssertSameTree(before, Path.Combine(destination, "Origen"));
    }

    [Fact]
    public void Reanudar_un_movimiento_ya_terminado_no_falla()
    {
        string file = _temp.Combine("suelto.bin");
        TestFiles.Create(file, 1234, 3);
        string destination = _temp.Combine("destino");
        var request = new CopyJobRequest([file], destination);

        new CopyEngine(Options(move: true)).Run(request);
        var again = new CopyEngine(Options(move: true)).Run(request);

        Assert.True(again.Success);
        Assert.True(File.Exists(Path.Combine(destination, "suelto.bin")));
    }

    [Fact]
    public void Mover_sobre_carpeta_existente_mezcla_y_respeta_lo_omitido()
    {
        string source = Tree("Origen");
        string destination = _temp.Combine("destino");
        Directory.CreateDirectory(Path.Combine(destination, "Origen", "sub"));
        File.WriteAllText(Path.Combine(destination, "Origen", "sub", "b.bin"), "otro contenido distinto");

        var options = Options(move: true);
        options.Conflict = ConflictPolicy.Skip;
        var result = new CopyEngine(options).Run(new CopyJobRequest([source], destination));

        Assert.Equal(1, result.FilesSkipped);
        Assert.False(File.Exists(Path.Combine(source, "a.bin")), "a.bin se movió");
        Assert.True(File.Exists(Path.Combine(source, "sub", "b.bin")), "b.bin se omitió y sigue en el origen");
        Assert.Equal("otro contenido distinto", File.ReadAllText(Path.Combine(destination, "Origen", "sub", "b.bin")));
    }

    [Fact]
    public void Preguntar_usa_la_respuesta_y_aplicar_a_todos()
    {
        string source = _temp.Combine("src");
        string destination = _temp.Combine("dst");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        foreach (string name in (string[])["1.txt", "2.txt", "3.txt"])
        {
            File.WriteAllText(Path.Combine(source, name), "nuevo " + name);
            File.WriteAllText(Path.Combine(destination, name), "viejo");
        }

        var options = Options();
        options.Conflict = ConflictPolicy.Ask;
        int asked = 0;
        var phases = new List<CopyPhase>();
        var engine = new CopyEngine(options)
        {
            ConflictResolver = (info, _) =>
            {
                asked++;
                Assert.Equal(5, info.DestinationLength);
                return asked == 1
                    ? new ConflictAnswer(ConflictDecision.Skip, ApplyToAll: false)
                    : new ConflictAnswer(ConflictDecision.KeepBoth, ApplyToAll: true);
            },
        };
        engine.ProgressChanged += p => phases.Add(p.Phase);

        var result = engine.Run(new CopyJobRequest(Directory.GetFiles(source).Order().ToList(), destination));

        Assert.Equal(2, asked); // la tercera usó "aplicar a todos"
        Assert.Contains(CopyPhase.WaitingForUser, phases);
        Assert.Equal("viejo", File.ReadAllText(Path.Combine(destination, "1.txt")));
        Assert.Equal("nuevo 2.txt", File.ReadAllText(Path.Combine(destination, "2 (2).txt")));
        Assert.Equal("nuevo 3.txt", File.ReadAllText(Path.Combine(destination, "3 (2).txt")));
        Assert.Equal(1, result.FilesSkipped);
    }

    [Fact]
    public void Preguntar_sin_quien_responda_omite()
    {
        string source = _temp.Combine("a.txt");
        string destination = _temp.Combine("dst");
        Directory.CreateDirectory(destination);
        File.WriteAllText(source, "nuevo, más largo");
        File.WriteAllText(Path.Combine(destination, "a.txt"), "viejo");

        var options = Options();
        options.Conflict = ConflictPolicy.Ask;
        var result = new CopyEngine(options).Run(new CopyJobRequest([source], destination));

        Assert.Equal(1, result.FilesSkipped);
        Assert.Equal("viejo", File.ReadAllText(Path.Combine(destination, "a.txt")));
    }

    [Fact]
    public void Limite_de_velocidad_se_respeta()
    {
        string source = _temp.Combine("lento.bin");
        TestFiles.Create(source, 3 * 1024 * 1024, 4);
        var options = Options();
        options.MaxBytesPerSecond = 4 * 1024 * 1024; // 3 MB a 4 MB/s ≈ 0,75 s

        var watch = Stopwatch.StartNew();
        var result = new CopyEngine(options).Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.InRange(watch.Elapsed.TotalSeconds, 0.6, 20); // lo que importa es el mínimo; el máximo solo detecta un cuelgue
    }
}
