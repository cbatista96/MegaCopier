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
}
