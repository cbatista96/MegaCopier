using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Tests;

/// <summary>Fase 5: clonado de bloques, copias en red, comprobación previa, rescate...</summary>
public sealed class TechnologyTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Options() => new() { ProgressInterval = TimeSpan.Zero, RetryDelay = TimeSpan.Zero };

    private string Write(string relative, string content)
    {
        string path = _temp.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void BlockCloneOnlyWhereTheVolumeSupportsIt()
    {
        string source = Write("src/grande.bin", new string('c', 300_000));
        Directory.CreateDirectory(_temp.Combine("dst"));
        bool refs = new DriveInfo(Path.GetPathRoot(_temp.Path)!).DriveFormat == "ReFS";
        using (var handle = File.OpenHandle(source))
        {
            Assert.Equal(refs, BlockClone.CanClone(handle, _temp.Combine("dst")));
        }

        var options = Options();
        options.Verify = true;
        var engine = new CopyEngine(options);
        var log = new List<LogEntry>();
        engine.Logged += log.Add;
        var result = engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.Equal(File.ReadAllText(source), File.ReadAllText(_temp.Combine("dst/grande.bin")));
        Assert.Equal(refs, log.Any(l => l.Message.Contains("clonados", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(@"\\nas\fotos\a.jpg", @"\\NAS\copias\b", true)]
    [InlineData(@"\\?\UNC\nas\fotos\a.jpg", @"\\nas\copias", true)]
    [InlineData(@"\\nas\fotos\a.jpg", @"\\otro\copias", false)]
    [InlineData(@"C:\fotos\a.jpg", @"\\nas\copias", false)]
    [InlineData(@"\\?\C:\fotos\a.jpg", @"\\?\C:\copias", false)]
    public void ServerSideCopyNeedsTheSameServer(string source, string destination, bool same) =>
        Assert.Equal(same, SystemCopy.SameServer(source, destination));

    [Fact]
    public void SystemCopyKeepsHashesVerificationAndDates()
    {
        string source = Write("src/datos.bin", new string('s', 3_000_000));
        File.SetLastWriteTimeUtc(source, new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc));
        var options = Options();
        options.ForceSystemCopy = true;
        options.Verify = true;
        options.HashAlgorithm = FileHashAlgorithm.Sha256;
        var engine = new CopyEngine(options);
        long lastCompleted = 0;
        engine.ProgressChanged += p => lastCompleted = p.CompletedBytes;

        var result = engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        string copy = _temp.Combine("dst/datos.bin");
        Assert.True(result.Success);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(copy));
        Assert.Equal(File.GetLastWriteTimeUtc(source), File.GetLastWriteTimeUtc(copy));
        var item = engine.Files.Snapshot().Single();
        Assert.Equal(FileState.Verified, item.State);
        Assert.Equal(item.SourceHash, item.DestinationHash);
        Assert.Equal(3_000_000, lastCompleted);
        Assert.False(File.Exists(copy + ".mcpart"));
    }

    [Fact]
    public void ManySmallFilesInParallelWithLargeOnesInBetween()
    {
        var expected = new Dictionary<string, string>();
        for (int i = 0; i < 300; i++)
        {
            string name = $"src/d{i % 7}/f{i}.txt";
            string content = i % 50 == 0 ? new string((char)('a' + i % 26), 6_000_000) : $"archivo {i}";
            Write(name, content);
            expected[name[4..]] = content;
        }

        var options = Options();
        options.ParallelSmallFiles = 8;
        options.Verify = true;
        var engine = new CopyEngine(options);
        CopyProgress? last = null;
        engine.ProgressChanged += p => last = p;

        var result = engine.Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.Equal(300, result.FilesCopied);
        Assert.Equal(expected.Values.Sum(v => (long)v.Length), result.BytesTransferred);
        Assert.All(expected, e => Assert.Equal(e.Value, File.ReadAllText(_temp.Combine("dst/src", e.Key))));
        Assert.All(engine.Files.Snapshot(), i => Assert.Equal(FileState.Verified, i.State));
        Assert.Equal(last!.Value.TotalBytes, last.Value.CompletedBytes);
        Assert.Equal(300, last.Value.FilesCompleted);
    }

    [Fact]
    public void SmallFastPathKeepsMetadataAndReplacesReadOnlyTargets()
    {
        string source = Write("src/nota.txt", "contenido nuevo");
        File.WriteAllText(source + ":extra", "flujo alternativo");
        File.SetCreationTimeUtc(source, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(source, new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc));
        File.SetAttributes(source, FileAttributes.ReadOnly | FileAttributes.Hidden);
        string target = Write("dst/nota.txt", "viejo");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        var options = Options();
        options.Conflict = ConflictPolicy.Overwrite;
        var result = new CopyEngine(options).Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.Equal("contenido nuevo", File.ReadAllText(target));
        Assert.Equal("flujo alternativo", File.ReadAllText(target + ":extra"));
        Assert.Equal(File.GetCreationTimeUtc(source), File.GetCreationTimeUtc(target));
        Assert.Equal(File.GetLastWriteTimeUtc(source), File.GetLastWriteTimeUtc(target));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, File.GetAttributes(target) & (FileAttributes.ReadOnly | FileAttributes.Hidden));
        Assert.Empty(Directory.GetFiles(_temp.Combine("dst"), "*.mcpart"));
        File.SetAttributes(source, FileAttributes.Normal);
        File.SetAttributes(target, FileAttributes.Normal);
    }

    [Fact]
    public void SparseFilesStaySparse()
    {
        string source = _temp.Combine("src/disco.vhd");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        const long size = 200L * 1024 * 1024;
        var data = Enumerable.Range(0, 1024 * 1024).Select(i => (byte)(i * 7 + 1)).ToArray();
        using (var handle = File.OpenHandle(source, FileMode.Create, FileAccess.ReadWrite))
        {
            Assert.True(Win32.SetSparse(handle));
            RandomAccess.SetLength(handle, size);
            RandomAccess.Write(handle, data, 0);
            RandomAccess.Write(handle, data, size - data.Length);
        }

        Assert.True(Win32.AllocatedSize(source) < 16L * 1024 * 1024);
        var options = Options();
        options.Verify = true;
        options.HashAlgorithm = FileHashAlgorithm.Sha256;
        var engine = new CopyEngine(options);
        var result = engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        string copy = _temp.Combine("dst/disco.vhd");
        Assert.True(result.Success);
        Assert.Equal(size, new FileInfo(copy).Length);
        Assert.True((File.GetAttributes(copy) & FileAttributes.SparseFile) != 0);
        Assert.True(Win32.AllocatedSize(copy) < 16L * 1024 * 1024, $"ocupa {Win32.AllocatedSize(copy)} bytes");
        var item = engine.Files.Snapshot().Single();
        Assert.Equal(FileState.Verified, item.State);
        Assert.Equal(item.SourceHash, item.DestinationHash);
    }

    [Fact]
    public void PreflightRejectsFilesTooLargeForFat32BeforeCopying()
    {
        string huge = _temp.Combine("src/pelicula.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(huge)!);
        using (var handle = File.OpenHandle(huge, FileMode.Create, FileAccess.ReadWrite))
        {
            Win32.SetSparse(handle);
            RandomAccess.SetLength(handle, 5L * 1024 * 1024 * 1024);
        }

        Write("src/foto.jpg", "foto");
        Write("src/CON .txt", "x");
        var engine = new CopyEngine(Options()) { FileSystemOverride = _ => "FAT32" };
        var log = new List<LogEntry>();
        engine.Logged += log.Add;
        var started = new List<string>();
        engine.Files.Changed += c =>
        {
            if (c.Kind == TransferListChangeKind.Updated && c.Item.State == FileState.Copying)
            {
                started.Add(Path.GetFileName(c.Item.Source));
            }
        };

        var result = engine.Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.DoesNotContain("pelicula.mkv", started); // ni lo intentó
        Assert.Single(result.Failures);
        Assert.Equal(FileErrorKind.TooLargeForFileSystem, result.Failures[0].Kind);
        Assert.True(File.Exists(_temp.Combine("dst/src/foto.jpg")));
        Assert.False(File.Exists(_temp.Combine("dst/src/pelicula.mkv")));
        Assert.Contains(log, l => l.Level == LogLevel.Warning && l.Message.Contains("FAT32", StringComparison.Ordinal));
        Assert.Contains(log, l => l.Level == LogLevel.Warning && l.Message.Contains("Nombre problemático", StringComparison.Ordinal));
    }

    [Fact]
    public void AllOrNothingFolderOnlyAppearsWhenComplete()
    {
        Write("src/a.txt", "a");
        string locked = Write("src/sub/b.txt", "b");
        var options = Options();
        options.AllOrNothing = true;
        options.RetryCount = 0;
        string final = _temp.Combine("dst/src");
        string staging = final + CopyEngine.StagingSuffix;

        using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var failed = new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));
            Assert.False(failed.Success);
        }

        Assert.False(Directory.Exists(final)); // nunca a medias
        Assert.True(Directory.Exists(staging));
        Assert.True((File.GetAttributes(staging) & FileAttributes.Hidden) != 0);
        Assert.True(File.Exists(Path.Combine(staging, "a.txt")));

        var retry = new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")) { Resumed = true });

        Assert.True(retry.Success);
        Assert.Equal(1, retry.FilesSkipped); // a.txt ya estaba en la carpeta oculta
        Assert.Equal("b", File.ReadAllText(Path.Combine(final, "sub/b.txt")));
        Assert.False(Directory.Exists(staging));
        Assert.Equal(0, (int)(File.GetAttributes(final) & FileAttributes.Hidden));
    }

    /// <summary>Sectores dañados simulados: las lecturas que tocan [inicio, fin) fallan con un error CRC.</summary>
    private sealed class BadSectors(long start, long end) : IFaultInjector
    {
        public void Hit(FaultPoint point, long chunkIndex, Microsoft.Win32.SafeHandles.SafeFileHandle destination)
        {
        }

        public void BeforeRead(long offset, int length)
        {
            if (offset < end && offset + length > start)
            {
                throw new IOException("Error de datos (comprobación de redundancia cíclica).", unchecked((int)0x80070017));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RescueModeSavesEverythingReadable(bool move)
    {
        var data = new byte[10_000_000];
        new Random(5).NextBytes(data);
        string source = _temp.Combine("src/video.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, data);
        var options = Options();
        options.RescueMode = true;
        options.Move = move;
        options.RetryCount = 0;
        var engine = new CopyEngine(options) { FaultInjector = new BadSectors(5_000_000, 5_010_000), SameVolumeOverride = (_, _) => false };

        var result = engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.Single(result.Failures);
        Assert.Equal(FileErrorKind.PartlyUnreadable, result.Failures[0].Kind);
        Assert.True(File.Exists(source)); // al mover, el original dañado no se borra
        byte[] copy = File.ReadAllBytes(_temp.Combine("dst/video.mp4"));
        Assert.Equal(data.Length, copy.Length);
        const int badStart = 4_997_120, badEnd = 5_013_504; // los trozos de 4 KB que tocan la zona dañada
        Assert.True(copy.AsSpan(0, badStart).SequenceEqual(data.AsSpan(0, badStart)));
        Assert.True(copy.AsSpan(badEnd).SequenceEqual(data.AsSpan(badEnd)));
        Assert.False(copy.AsSpan(badStart, badEnd - badStart).ContainsAnyExcept((byte)0));
    }

    [Fact]
    public void WithoutRescueModeBadSectorsFailTheFile()
    {
        string source = Write("src/video.mp4", new string('v', 6_000_000));
        var options = Options();
        options.RetryCount = 0;
        var engine = new CopyEngine(options) { FaultInjector = new BadSectors(5_000_000, 5_010_000) };

        var result = engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.Single(result.Failures);
        Assert.NotEqual(FileErrorKind.PartlyUnreadable, result.Failures[0].Kind);
        Assert.False(File.Exists(_temp.Combine("dst/video.mp4")));
    }

    /// <summary>Cambia el contenido sin reescribir el archivo (así se conservan sus flujos alternativos).</summary>
    private static void Overwrite(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.SetLength(0);
        stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    public void IntegrityGuardTellsEditsFromCorruption()
    {
        string edited = Write("fotos/editada.jpg", "original");
        string rotten = Write("fotos/podrida.jpg", "intacta");
        Write("fotos/buena.jpg", "buena");
        var protect = Options();
        protect.Operation = JobOperation.Hash;
        protect.SaveHashStream = true;
        protect.HashAlgorithm = FileHashAlgorithm.Sha256;
        Assert.Equal(3, new CopyEngine(protect).Run(new CopyJobRequest([_temp.Combine("fotos")], _temp.Combine("fotos"))).FilesCopied);
        Assert.Empty(Directory.GetFiles(_temp.Combine("fotos"), "*.sha256")); // sin lista: el hash va dentro

        Overwrite(edited, "retocada"); // edición normal: cambia la fecha
        File.SetLastWriteTimeUtc(edited, DateTime.UtcNow.AddMinutes(5));
        var stamp = File.GetLastWriteTimeUtc(rotten);
        Overwrite(rotten, "dañada!"); // corrupción silenciosa: misma fecha
        File.SetLastWriteTimeUtc(rotten, stamp);

        var check = Options();
        check.Operation = JobOperation.Test;
        var first = new CopyEngine(check);
        var result = first.Run(new CopyJobRequest([_temp.Combine("fotos")], _temp.Combine("fotos")));
        Assert.Single(result.Failures);
        Assert.Equal(Path.GetFullPath(rotten), result.Failures[0].Source);
        Assert.Equal(FileErrorKind.ChecksumMismatch, result.Failures[0].Kind);
        Assert.Equal(1, result.FilesSkipped);
        Assert.Equal(1, result.FilesCopied);

        string added = Write("fotos/nueva.jpg", "nueva"); // sin hash todavía

        // Comprobar renovando los hashes de lo editado: la próxima vez la editada ya está protegida.
        check.SaveHashStream = true;
        new CopyEngine(check).Run(new CopyJobRequest([_temp.Combine("fotos")], _temp.Combine("fotos")));
        check.SaveHashStream = false;
        var again = new CopyEngine(check).Run(new CopyJobRequest([_temp.Combine("fotos")], _temp.Combine("fotos")));
        Assert.Equal(3, again.FilesCopied);
        Assert.NotNull(CopyEngine.ReadHashStream(added));
        Assert.Equal(0, again.FilesSkipped);
    }

    [Theory]
    [InlineData(0, "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262")]
    [InlineData(1025, "d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444")]
    [InlineData(1_000_003, "cd5a3272e01b1a2f47bb4565d8d202db0f95704d32550a2da61a0fd363d4c90d")]
    public void Blake3MatchesTheOfficialImplementation(int length, string expected)
    {
        // Valores de la implementación oficial en Rust (Blake3.Native) para bytes i % 251.
        var data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        string source = _temp.Combine("src/datos.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, data);
        var options = Options();
        options.HashAlgorithm = FileHashAlgorithm.Blake3;
        options.SaveChecksumFile = true;
        options.Verify = true;
        var engine = new CopyEngine(options);
        engine.Run(new CopyJobRequest([source], _temp.Combine("dst")));

        Assert.Equal(expected, engine.Files.Snapshot().Single().SourceHash);
        Assert.EndsWith(".b3", engine.ChecksumFile);
        var test = Options();
        test.Operation = JobOperation.Test;
        Assert.True(new CopyEngine(test).Run(new CopyJobRequest([engine.ChecksumFile!], _temp.Combine("dst"))).Success);
    }

    [Fact]
    public void SystemCopyCanBeCancelled()
    {
        string source = Write("src/enorme.bin", "");
        using (var stream = File.OpenWrite(source))
        {
            stream.SetLength(400L * 1024 * 1024);
        }

        var options = Options();
        options.ForceSystemCopy = true;
        options.UseUnbufferedIo = false;
        using var cancellation = new CancellationTokenSource();
        var engine = new CopyEngine(options);
        engine.ProgressChanged += p =>
        {
            if (p.CompletedBytes > 0)
            {
                cancellation.Cancel();
            }
        };

        Assert.ThrowsAny<OperationCanceledException>(() => engine.Run(new CopyJobRequest([source], _temp.Combine("dst")), cancellation.Token));
        Assert.False(File.Exists(_temp.Combine("dst/enorme.bin")));
    }
}
