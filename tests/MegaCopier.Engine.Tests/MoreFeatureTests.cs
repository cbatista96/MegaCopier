using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Tests;

/// <summary>Sincronizar, simular, versiones, modos, tolerancia, hash en el archivo, enlaces duros y esperas.</summary>
public sealed class MoreFeatureTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Options() => new() { ProgressInterval = TimeSpan.Zero, RetryDelay = TimeSpan.Zero, WaitPollInterval = TimeSpan.FromMilliseconds(100) };

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

    [Fact]
    public void MirrorDeletesExtrasKeepsFilteredOutAndVersions()
    {
        Write("src/a.txt", "a");
        Write("src/sub/b.txt", "b");
        Write("dst/src/a.txt", "viejo", DateTime.UtcNow.AddDays(-3));
        Write("dst/src/sobra.txt", "x");
        Write("dst/src/carpeta-sobrante/c.txt", "c");
        Write("dst/src/notas.tmp", "fuera de los filtros");

        var options = Options();
        options.Mode = CopyMode.Mirror;
        options.Conflict = ConflictPolicy.Overwrite;
        options.VersionsFolder = ".versiones";
        options.Filter = new FileFilter { Exclude = ["*.tmp"] };

        // Primero simulando: no cambia nada.
        var dry = Options();
        dry.Mode = CopyMode.Mirror;
        dry.DryRun = true;
        dry.Filter = options.Filter;
        var simulation = new CopyEngine(dry);
        var simulated = simulation.Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));
        Assert.Equal(2, simulated.FilesDeleted);
        Assert.True(File.Exists(_temp.Combine("dst/src/sobra.txt")));
        Assert.All(simulation.Files.Snapshot(), i => Assert.Equal(FileState.Planned, i.State));
        Assert.Contains(simulation.Files.Snapshot(), i => i.Action == PlannedAction.Replace && i.Source.EndsWith("a.txt"));
        Assert.Contains(simulation.Files.Snapshot(), i => i.Action == PlannedAction.Copy && i.Source.EndsWith("b.txt"));

        var result = new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.Equal(2, result.FilesDeleted);
        Assert.False(File.Exists(_temp.Combine("dst/src/sobra.txt")));
        Assert.False(Directory.Exists(_temp.Combine("dst/src/carpeta-sobrante")));
        Assert.True(File.Exists(_temp.Combine("dst/src/notas.tmp"))); // los filtros lo excluyen: no se toca
        Assert.Equal("a", File.ReadAllText(_temp.Combine("dst/src/a.txt")));
        var versions = Directory.GetFiles(_temp.Combine("dst/.versiones"), "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();
        Assert.Contains(versions, v => v!.StartsWith("a ") && v.EndsWith(".txt"));
        Assert.Contains(versions, v => v!.StartsWith("sobra "));
    }

    [Fact]
    public void UpdateExistingOnlyAndFoldersOnly()
    {
        Write("src/existe.txt", "nuevo");
        Write("src/nuevo.txt", "n");
        Write("src/vacia/x.txt", "x");
        Write("dst/src/existe.txt", "viejo", DateTime.UtcNow.AddDays(-1));

        var update = Options();
        update.Mode = CopyMode.UpdateExisting;
        update.Conflict = ConflictPolicy.Overwrite;
        new CopyEngine(update).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));
        Assert.Equal("nuevo", File.ReadAllText(_temp.Combine("dst/src/existe.txt")));
        Assert.False(File.Exists(_temp.Combine("dst/src/nuevo.txt")));

        var folders = Options();
        folders.Mode = CopyMode.FoldersOnly;
        new CopyEngine(folders).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst2")));
        Assert.True(Directory.Exists(_temp.Combine("dst2/src/vacia")));
        Assert.Empty(Directory.GetFiles(_temp.Combine("dst2"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ExistingFolderSkippedOnlyOnFirstRun()
    {
        Write("src/a.txt", "a");
        Directory.CreateDirectory(_temp.Combine("dst/src"));
        var options = Options();
        options.FolderConflict = FolderConflictPolicy.Skip;

        new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));
        Assert.False(File.Exists(_temp.Combine("dst/src/a.txt")));

        new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")) { Resumed = true });
        Assert.True(File.Exists(_temp.Combine("dst/src/a.txt")));
    }

    [Fact]
    public void TimestampsWithinToleranceAreIdentical()
    {
        var when = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        string source = Write("src/f.txt", "abc", when);
        Write("dst/f.txt", "xyz", when.AddSeconds(1)); // como en FAT: hora redondeada

        var result = new CopyEngine(Options()).Run(new CopyJobRequest([source], _temp.Combine("dst")));
        Assert.Equal(1, result.FilesSkipped);
        Assert.Equal("xyz", File.ReadAllText(_temp.Combine("dst/f.txt")));
    }

    [Fact]
    public void HashStreamDetectsLaterCorruption()
    {
        string source = Write("src/datos.bin", new string('d', 100_000));
        var options = Options();
        options.SaveHashStream = true;
        options.HashAlgorithm = FileHashAlgorithm.Sha256;
        options.Verify = true;
        options.VerifySource = true;
        var copy = new CopyEngine(options).Run(new CopyJobRequest([source], _temp.Combine("dst")));
        Assert.True(copy.Success);

        string copied = _temp.Combine("dst/datos.bin");
        Assert.Equal(File.GetLastWriteTimeUtc(source), File.GetLastWriteTimeUtc(copied));
        Assert.Equal(FileHashAlgorithm.Sha256, CopyEngine.ReadHashStream(copied)!.Value.Algorithm);

        var test = Options();
        test.Operation = JobOperation.Test;
        Assert.True(new CopyEngine(test).Run(new CopyJobRequest([_temp.Combine("dst")], _temp.Combine("dst"))).Success);

        // "Corrupción silenciosa": cambia un byte sin tocar la fecha.
        var stamp = File.GetLastWriteTimeUtc(copied);
        using (var stream = File.OpenWrite(copied))
        {
            stream.Position = 500;
            stream.WriteByte((byte)'X');
        }

        File.SetLastWriteTimeUtc(copied, stamp);
        var broken = new CopyEngine(test).Run(new CopyJobRequest([_temp.Combine("dst")], _temp.Combine("dst")));
        Assert.Single(broken.Failures);
        Assert.Equal(FileErrorKind.ChecksumMismatch, broken.Failures[0].Kind);
    }

    [Fact]
    public void HardLinksArePreserved()
    {
        string original = Write("src/uno.bin", new string('h', 50_000));
        Links.CreateHardLink(_temp.Combine("src/dos.bin"), original);
        var options = Options();
        options.PreserveHardLinks = true;

        var result = new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success);
        var a = Links.GetLinkInfo(_temp.Combine("dst/src/uno.bin"))!.Value;
        var b = Links.GetLinkInfo(_temp.Combine("dst/src/dos.bin"))!.Value;
        Assert.Equal(2u, a.Count);
        Assert.Equal(a.Index, b.Index);
    }

    [Fact]
    public void HashOperationWritesChecksumFile()
    {
        Write("carpeta/a.txt", "a");
        Write("carpeta/sub/b.txt", "b");
        var options = Options();
        options.Operation = JobOperation.Hash;
        options.HashAlgorithm = FileHashAlgorithm.Sha256;
        var engine = new CopyEngine(options);
        var result = engine.Run(new CopyJobRequest([_temp.Combine("carpeta")], _temp.Path));

        Assert.Equal(2, result.FilesCopied);
        Assert.Equal(_temp.Combine("carpeta", "carpeta.sha256"), engine.ChecksumFile);

        var test = Options();
        test.Operation = JobOperation.Test;
        var tested = new CopyEngine(test).Run(new CopyJobRequest([engine.ChecksumFile!], _temp.Path));
        Assert.True(tested.Success);
        Assert.Equal(2, tested.FilesCopied);
    }

    [Fact]
    public void SecureMoveWipesOriginals()
    {
        string source = Write("src/secreto.txt", "confidencial");
        var options = Options();
        options.Move = true;
        options.WipeSource = true;
        var engine = new CopyEngine(options) { SameVolumeOverride = (_, _) => false };

        var result = engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.False(File.Exists(source));
        Assert.Empty(Directory.GetFiles(_temp.Combine("src")));
        Assert.Equal("confidencial", File.ReadAllText(_temp.Combine("dst/secreto.txt")));
    }

    [Fact]
    public void FailedFileIsRetriedAtTheEnd()
    {
        string locked = Write("src/a.txt", "a");
        Write("src/b.txt", "b");
        var options = Options();
        options.RetryCount = 0;
        options.OnError = ErrorPolicy.MoveToEnd;
        var engine = new CopyEngine(options);
        var order = new List<string>();
        engine.Files.Changed += c =>
        {
            if (c.Kind == TransferListChangeKind.Updated && c.Item.State == FileState.Copying)
            {
                order.Add(Path.GetFileName(c.Item.Source));
            }
        };

        CopyJobResult result;
        using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = engine.Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));
        }

        Assert.Equal(["a.txt", "b.txt", "a.txt"], order);
        Assert.Single(result.Failures);
        Assert.Equal(FileErrorKind.InUse, result.Failures[0].Kind);
    }

    [Fact]
    public async Task WaitsForADisconnectedSourceToComeBack()
    {
        Write("uno/a.txt", new string('a', 10));
        Write("dos/b.txt", new string('b', 10));
        var engine = new CopyEngine(Options());
        Task? restore = null;
        engine.Files.Changed += c =>
        {
            // Al empezar el primero, "se desconecta" la carpeta del segundo y vuelve 1 s después.
            if (restore is null && c.Kind == TransferListChangeKind.Updated && c.Item.State == FileState.Copying)
            {
                Directory.Move(_temp.Combine("dos"), _temp.Combine("dos-fuera"));
                restore = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    Directory.Move(_temp.Combine("dos-fuera"), _temp.Combine("dos"));
                });
            }
        };

        var phases = new List<CopyPhase>();
        engine.ProgressChanged += p => phases.Add(p.Phase);
        var result = await engine.RunAsync(new CopyJobRequest([_temp.Combine("uno"), _temp.Combine("dos")], _temp.Combine("dst")));
        await restore!;

        Assert.True(result.Success, string.Join("; ", result.Failures.Select(f => f.Message)));
        Assert.True(File.Exists(_temp.Combine("dst/dos/b.txt")));
        Assert.Contains(CopyPhase.WaitingForDevice, phases);
    }

    [Fact]
    public void JunkFilesAreSkipped()
    {
        Write("src/foto.jpg", "f");
        Write("src/Thumbs.db", "t");
        Write("src/desktop.ini", "d");
        Write("src/~$informe.docx", "lock");
        Write("src/.DS_Store", "m");
        var options = Options();
        options.Filter = new FileFilter { SkipJunk = true };

        new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.Equal(["foto.jpg"], Directory.GetFiles(_temp.Combine("dst/src")).Select(Path.GetFileName));
    }

    [Fact]
    public void Sha3IsAvailableWhenTheSystemSupportsIt()
    {
        if (!System.Security.Cryptography.SHA3_256.IsSupported)
        {
            return;
        }

        string source = Write("src/s.txt", "sha3");
        var options = Options();
        options.HashAlgorithm = FileHashAlgorithm.Sha3_256;
        options.SaveChecksumFile = true;
        var engine = new CopyEngine(options);
        engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));
        Assert.EndsWith(".sha3-256", engine.ChecksumFile);

        var test = Options();
        test.Operation = JobOperation.Test;
        Assert.True(new CopyEngine(test).Run(new CopyJobRequest([engine.ChecksumFile!], _temp.Combine("dst"))).Success);
    }
}
