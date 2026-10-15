using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

public sealed class JobTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Options() => new()
    {
        BlockSize = 64 * 1024,
        ChunkSize = 256 * 1024,
        ResumableThreshold = 512 * 1024,
        RetryDelay = TimeSpan.Zero,
    };

    private string BuildTree()
    {
        string root = _temp.Combine("Proyecto");
        Directory.CreateDirectory(Path.Combine(root, "sub", "profundo"));
        Directory.CreateDirectory(Path.Combine(root, "vacia"));
        TestFiles.Create(Path.Combine(root, "a.bin"), 10_000, 1);
        TestFiles.Create(Path.Combine(root, "sub", "b.bin"), 2 * 1024 * 1024 + 77, 2);
        TestFiles.Create(Path.Combine(root, "sub", "profundo", "c.bin"), 5, 3);
        string hidden = Path.Combine(root, "oculto.txt");
        File.WriteAllText(hidden, "secreto");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        return root;
    }

    [Fact]
    public void Copia_carpetas_completas_con_atributos_y_vacias()
    {
        string root = BuildTree();
        string destination = _temp.Combine("destino");

        var result = new CopyEngine(Options()).Run(new CopyJobRequest([root], destination));

        Assert.True(result.Success);
        Assert.Equal(4, result.FilesCopied);
        string copy = Path.Combine(destination, "Proyecto");
        TestFiles.AssertSameContent(Path.Combine(root, "a.bin"), Path.Combine(copy, "a.bin"));
        TestFiles.AssertSameContent(Path.Combine(root, "sub", "b.bin"), Path.Combine(copy, "sub", "b.bin"));
        TestFiles.AssertSameContent(Path.Combine(root, "sub", "profundo", "c.bin"), Path.Combine(copy, "sub", "profundo", "c.bin"));
        Assert.True(Directory.Exists(Path.Combine(copy, "vacia")));
        Assert.True(File.GetAttributes(Path.Combine(copy, "oculto.txt")).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void Repetir_el_trabajo_omite_lo_ya_copiado()
    {
        string root = BuildTree();
        string destination = _temp.Combine("destino");
        new CopyEngine(Options()).Run(new CopyJobRequest([root], destination));

        var second = new CopyEngine(Options()).Run(new CopyJobRequest([root], destination));

        Assert.Equal(0, second.FilesCopied);
        Assert.Equal(4, second.FilesSkipped);
        Assert.Equal(0, second.BytesTransferred);
    }

    [Fact]
    public void Trabajo_cortado_a_mitad_se_completa_al_reanudar()
    {
        string root = BuildTree();
        string destination = _temp.Combine("destino");

        Assert.Throws<SimulatedPowerLoss>(() =>
            new CopyEngine(Options()) { FaultInjector = new CrashAt(FaultPoint.AfterJournalAppend, 3) }
                .Run(new CopyJobRequest([root], destination)));

        var result = new CopyEngine(Options()).Run(new CopyJobRequest([root], destination));

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesResumed);
        TestFiles.AssertSameContent(Path.Combine(root, "sub", "b.bin"), Path.Combine(destination, "Proyecto", "sub", "b.bin"));
    }

    [Fact]
    public void Conflicto_skip_no_toca_el_existente()
    {
        string source = _temp.Combine("f.txt");
        string destination = _temp.Combine("destino");
        Directory.CreateDirectory(destination);
        File.WriteAllText(source, "nuevo");
        File.WriteAllText(Path.Combine(destination, "f.txt"), "viejo");

        var result = new CopyEngine(new CopyOptions { Conflict = ConflictPolicy.Skip }).Run(new CopyJobRequest([source], destination));

        Assert.Equal(1, result.FilesSkipped);
        Assert.Equal("viejo", File.ReadAllText(Path.Combine(destination, "f.txt")));
    }

    [Fact]
    public void Conflicto_rename_conserva_ambos_y_no_duplica_al_repetir()
    {
        string source = _temp.Combine("f.txt");
        string destination = _temp.Combine("destino");
        Directory.CreateDirectory(destination);
        File.WriteAllText(source, "contenido nuevo");
        File.WriteAllText(Path.Combine(destination, "f.txt"), "viejo");
        var options = new CopyOptions { Conflict = ConflictPolicy.KeepBoth };

        new CopyEngine(options).Run(new CopyJobRequest([source], destination));
        var again = new CopyEngine(options).Run(new CopyJobRequest([source], destination));

        Assert.Equal("viejo", File.ReadAllText(Path.Combine(destination, "f.txt")));
        Assert.Equal("contenido nuevo", File.ReadAllText(Path.Combine(destination, "f (2).txt")));
        Assert.False(File.Exists(Path.Combine(destination, "f (3).txt")));
        Assert.Equal(1, again.FilesSkipped);
    }

    [Fact]
    public void Archivo_suelto_a_carpeta_inexistente_la_crea()
    {
        string source = _temp.Combine("suelto.txt");
        File.WriteAllText(source, "hola");
        string destination = _temp.Combine("no", "existe", "aun");

        var result = new CopyEngine(Options()).Run(new CopyJobRequest([source], destination));

        Assert.True(result.Success);
        Assert.Equal("hola", File.ReadAllText(Path.Combine(destination, "suelto.txt")));
    }

    [Fact]
    public void No_permite_copiar_una_carpeta_dentro_de_si_misma()
    {
        string root = BuildTree();

        Assert.Throws<CopyPlanException>(() => new CopyEngine(Options()).Run(new CopyJobRequest([root], Path.Combine(root, "sub"))));
    }

    [Fact]
    public void Origen_inexistente_es_error_de_plan()
    {
        Assert.Throws<CopyPlanException>(() =>
            new CopyEngine().Run(new CopyJobRequest([_temp.Combine("no-existe")], _temp.Combine("destino"))));
    }

    [Fact]
    public void JobStore_guarda_y_recupera()
    {
        var store = new JobStore(_temp.Combine("jobs"));
        var job = CopyJobRecord.New(["C:\\a", "C:\\b"], "D:\\x", new CopyOptions { Verify = true, Conflict = ConflictPolicy.KeepBoth });

        store.Save(job);
        var loaded = store.Get(job.Id);

        Assert.NotNull(loaded);
        Assert.Equal(job.Sources, loaded.Sources);
        Assert.True(loaded.Verify);
        Assert.Equal(ConflictPolicy.KeepBoth, loaded.Conflict);
        Assert.Single(store.List());

        store.Delete(job.Id);
        Assert.Empty(store.List());
    }
}
