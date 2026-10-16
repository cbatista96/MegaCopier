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
