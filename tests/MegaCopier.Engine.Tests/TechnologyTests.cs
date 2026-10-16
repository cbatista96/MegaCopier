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
