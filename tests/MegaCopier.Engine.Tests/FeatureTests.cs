using System.Security.Cryptography;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

/// <summary>Reglas de conflicto nuevas, comprobar sumas, borrar, enlaces, rutas largas y programación.</summary>
public sealed class FeatureTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Options() => new() { ProgressInterval = TimeSpan.Zero, RetryDelay = TimeSpan.Zero };

    private string Write(string relative, string content, DateTime? lastWrite = null)
    {
        string path = _temp.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (lastWrite is { } time)
        {
            File.SetLastWriteTimeUtc(path, time);
        }

        return path;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Conflictos

    [Fact]
    public void OverwriteIfDifferentSize()
    {
        var old = DateTime.UtcNow.AddDays(-2);
        string same = Write("src/same.txt", "AAAA", DateTime.UtcNow);
        string bigger = Write("src/bigger.txt", "AAAAAAAA", DateTime.UtcNow);
        Write("dst/same.txt", "BBBB", old);
        Write("dst/bigger.txt", "BB", old);

        var options = Options();
        options.Conflict = ConflictPolicy.OverwriteIfDifferentSize;
        new CopyEngine(options).Run(new CopyJobRequest([same, bigger], _temp.Combine("dst")));

        Assert.Equal("BBBB", File.ReadAllText(_temp.Combine("dst/same.txt")));
        Assert.Equal("AAAAAAAA", File.ReadAllText(_temp.Combine("dst/bigger.txt")));
    }

    [Fact]
    public void RenameExistingKeepsOldFileWithPattern()
    {
        string source = Write("src/doc.txt", "nuevo");
        Write("dst/doc.txt", "viejo", DateTime.UtcNow.AddDays(-1));

        var options = Options();
        options.Conflict = ConflictPolicy.RenameExisting;
        options.KeepBothPattern = "{name} - anterior {n}{ext}";
        var result = new CopyEngine(options).Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.Equal("nuevo", File.ReadAllText(_temp.Combine("dst/doc.txt")));
        Assert.Equal("viejo", File.ReadAllText(_temp.Combine("dst/doc - anterior 2.txt")));
    }

    [Fact]
    public void KeepBothUsesPatternAndSanitizes()
    {
        string source = Write("src/foto.jpg", "x2");
        Write("dst/foto.jpg", "x1", DateTime.UtcNow.AddDays(-1));

        var options = Options();
        options.Conflict = ConflictPolicy.KeepBoth;
        options.KeepBothPattern = "{name}_copia:{n}{ext}"; // ":" no es válido en un nombre
        new CopyEngine(options).Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.Equal("x2", File.ReadAllText(_temp.Combine("dst/foto_copia_2.jpg")));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Comprobar sumas

    [Fact]
    public void TestModeChecksSeveralFormats()
    {
        string a = Write("datos/a.bin", "contenido A");
        string b = Write("datos/sub/b.bin", "contenido B");
        string sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(a)));
        string shaB = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(b)));
        string md5 = Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(a)));

        // sha256sum con barra de Unix, un archivo cambiado y uno que falta.
        Write("datos/sumas.sha256", $"{sha} *a.bin\n{shaB}  sub/b.bin\n{new string('0', 64)} *falta.bin\n");
        Write("datos/bsd.md5", $"MD5 (a.bin) = {md5.ToUpperInvariant()}\n");

        var options = Options();
        options.Operation = JobOperation.Test;
        var engine = new CopyEngine(options);
        File.AppendAllText(b, " cambiado");
        var result = engine.Run(new CopyJobRequest([_temp.Combine("datos")], _temp.Combine("datos")));

        var items = engine.Files.Snapshot().ToDictionary(i => Path.GetFileName(i.Source) + "|" + i.DestinationHash![..4]);
        Assert.Equal(2, result.FilesCopied); // a.bin por SHA-256 y por MD5
        Assert.Equal(2, result.Failures.Count);
        Assert.Contains(result.Failures, f => f.Kind == FileErrorKind.ChecksumMismatch && f.Source.EndsWith("b.bin"));
        Assert.Contains(result.Failures, f => f.Kind == FileErrorKind.NotFound && f.Source.EndsWith("falta.bin"));
        Assert.All(engine.Files.Snapshot().Where(i => Path.GetFileName(i.Source) == "a.bin"), i => Assert.Equal(FileState.Verified, i.State));
    }

    [Fact]
    public void TestModeReadsOwnChecksumFiles()
    {
        string source = Write("src/x.dat", new string('z', 5000));
        var copy = Options();
        copy.SaveChecksumFile = true;
        copy.HashAlgorithm = FileHashAlgorithm.Crc32; // formato .sfv
        var copier = new CopyEngine(copy);
        copier.Run(new CopyJobRequest([source], _temp.Combine("dst")));
        Assert.EndsWith(".sfv", copier.ChecksumFile);

        var test = Options();
        test.Operation = JobOperation.Test;
        var result = new CopyEngine(test).Run(new CopyJobRequest([copier.ChecksumFile!], _temp.Combine("dst")));
        Assert.True(result.Success);
        Assert.Equal(1, result.FilesCopied);
    }

    [Fact]
    public void TestModeWithoutChecksumFilesFails() =>
        Assert.Throws<CopyPlanException>(() =>
        {
            var options = Options();
            options.Operation = JobOperation.Test;
            Write("vacio/nada.txt", "x");
            new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("vacio")], _temp.Combine("vacio")));
        });

    // ---------------------------------------------------------------------------------------------------------
    // Borrar

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteRemovesFilesAndFolders(bool secure)
    {
        Write("borrar/a.txt", "a");
        string readOnly = Write("borrar/sub/b.txt", new string('b', 3_000_000));
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        Write("conservar/c.txt", "c");

        var options = Options();
        options.Operation = JobOperation.Delete;
        options.SecureDelete = secure;
        var result = new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("borrar")], _temp.Path));

        Assert.True(result.Success);
        Assert.Equal(2, result.FilesCopied);
        Assert.False(Directory.Exists(_temp.Combine("borrar")));
        Assert.True(File.Exists(_temp.Combine("conservar/c.txt")));
    }

    [Fact]
    public void DeleteNeverFollowsLinks()
    {
        Write("importante/datos.txt", "no tocar");
        Directory.CreateDirectory(_temp.Combine("borrar"));
        Native.Links.CreateJunction(_temp.Combine("borrar/union"), _temp.Combine("importante"));

        var options = Options();
        options.Operation = JobOperation.Delete;
        new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("borrar")], _temp.Path));

        Assert.False(Directory.Exists(_temp.Combine("borrar")));
        Assert.True(File.Exists(_temp.Combine("importante/datos.txt")));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Enlaces

    [Fact]
    public void JunctionIsRecreatedAsLink()
    {
        Write("destino-real/x.txt", "x");
        Directory.CreateDirectory(_temp.Combine("src"));
        Native.Links.CreateJunction(_temp.Combine("src/atajo"), _temp.Combine("destino-real"));
        Write("src/normal.txt", "n");

        var result = new CopyEngine(Options()).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success);
        var link = new DirectoryInfo(_temp.Combine("dst/src/atajo"));
        Assert.NotNull(link.LinkTarget);
        Assert.Equal(_temp.Combine("destino-real"), Path.TrimEndingDirectorySeparator(link.LinkTarget!));
        Assert.True(File.Exists(_temp.Combine("dst/src/normal.txt")));
    }

    [Fact]
    public void FollowTargetCopiesContentAndAvoidsLoops()
    {
        Write("src/real/x.txt", "x");
        Native.Links.CreateJunction(_temp.Combine("src/real/bucle"), _temp.Combine("src")); // apunta a su abuelo
        Native.Links.CreateJunction(_temp.Combine("src/atajo"), _temp.Combine("src/real"));

        var options = Options();
        options.Links = LinkHandling.FollowTarget;
        var engine = new CopyEngine(options);
        var result = engine.Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.True(File.Exists(_temp.Combine("dst/src/real/x.txt")));
        var atajo = new DirectoryInfo(_temp.Combine("dst/src/atajo"));
        Assert.Null(atajo.LinkTarget); // carpeta normal, no enlace
        Assert.True(File.Exists(_temp.Combine("dst/src/atajo/x.txt")));
        Assert.False(Directory.Exists(_temp.Combine("dst/src/real/bucle/real"))); // el bucle se cortó
    }

    [Fact]
    public void FileSymlinkIsCopiedAsLinkOrContent()
    {
        string real = Write("real.txt", "contenido real");
        Directory.CreateDirectory(_temp.Combine("src"));
        try
        {
            File.CreateSymbolicLink(_temp.Combine("src/enlace.txt"), real);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // sin modo de desarrollador ni administrador no se puede preparar la prueba
        }

        var result = new CopyEngine(Options()).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.Equal("contenido real", File.ReadAllText(_temp.Combine("dst/src/enlace.txt")));
    }

    [Fact]
    public void SkipLinksIgnoresThem()
    {
        Write("real/x.txt", "x");
        Directory.CreateDirectory(_temp.Combine("src"));
        Native.Links.CreateJunction(_temp.Combine("src/atajo"), _temp.Combine("real"));
        Write("src/n.txt", "n");

        var options = Options();
        options.Links = LinkHandling.Skip;
        new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.False(Directory.Exists(_temp.Combine("dst/src/atajo")));
        Assert.True(File.Exists(_temp.Combine("dst/src/n.txt")));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Rutas largas

    [Fact]
    public void LongPathsCopyWithStreamsAndAcl()
    {
        string deep = string.Join(Path.DirectorySeparatorChar, Enumerable.Range(0, 12).Select(i => $"carpeta-con-nombre-largo-{i:00}"));
        string source = _temp.Combine("src", deep, "archivo.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "largo");
        File.WriteAllText(source + ":extra", "flujo");
        Assert.True(source.Length > 300);

        var options = Options();
        options.CopySecurity = Native.SecurityParts.Acl;
        var result = new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success, string.Join("; ", result.Failures.Select(f => f.Message)));
        string target = _temp.Combine("dst", "src", deep, "archivo.txt");
        Assert.Equal("largo", File.ReadAllText(target));
        Assert.Equal("flujo", File.ReadAllText(target + ":extra"));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Programación

    [Fact]
    public async Task ScheduledJobWaitsUntilItsTime()
    {
        string source = Write("src/p.txt", "programado");
        var queue = new CopyQueue(new JobStore(_temp.Combine("jobs")), _ => Options());
        var job = queue.Submit([source], _temp.Combine("dst"), Options(), DateTime.UtcNow.AddSeconds(6));

        await Task.Delay(2000);
        Assert.Equal(CopyJobState.Queued, job.State);
        Assert.False(File.Exists(_temp.Combine("dst/p.txt")));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (job.State != CopyJobState.Completed && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal(CopyJobState.Completed, job.State);
        Assert.Equal("programado", File.ReadAllText(_temp.Combine("dst/p.txt")));
        Assert.Null(job.Record.StartAtUtc);
    }

    [Fact]
    public void RecordKeepsNewOptions()
    {
        var options = Options();
        options.Operation = JobOperation.Delete;
        options.SecureDelete = true;
        options.Links = LinkHandling.Skip;
        options.KeepBothPattern = "{name}-{n}{ext}";
        options.Filter = new FileFilter { Exclude = ["*.tmp"], MinSize = 10 };
        options.RetryCount = 7;
        options.OnError = ErrorPolicy.Stop;

        var restored = CopyJobRecord.FromJson(CopyJobRecord.New(["a"], "b", options).ToJson())!.ToOptions();
        Assert.Equal(JobOperation.Delete, restored.Operation);
        Assert.True(restored.SecureDelete);
        Assert.Equal(LinkHandling.Skip, restored.Links);
        Assert.Equal("{name}-{n}{ext}", restored.KeepBothPattern);
        Assert.Equal(["*.tmp"], restored.Filter.Exclude);
        Assert.Equal(10, restored.Filter.MinSize);
        Assert.Equal(7, restored.RetryCount);
        Assert.Equal(ErrorPolicy.Stop, restored.OnError);
    }
}
