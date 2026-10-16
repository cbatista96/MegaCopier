using System.Diagnostics;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Tests;

public sealed class IoRingTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private List<string> MakeFiles(int count, int seed)
    {
        var random = new Random(seed);
        var paths = new List<string>();
        Directory.CreateDirectory(_temp.Combine("f"));
        for (int i = 0; i < count; i++)
        {
            byte[] data = new byte[i % 7 == 0 ? 0 : random.Next(1, 200_000)];
            random.NextBytes(data);
            string path = _temp.Combine($"f/{i:D5}.bin");
            File.WriteAllBytes(path, data);
            paths.Add(path);
        }

        return paths;
    }

    private static unsafe void WriteCold(string folder, int count, int seed)
    {
        Directory.CreateDirectory(folder);
        var random = new Random(seed);
        byte* memory = (byte*)System.Runtime.InteropServices.NativeMemory.AlignedAlloc(256 * 1024, 4096);
        try
        {
            var span = new Span<byte>(memory, 256 * 1024);
            for (int i = 0; i < count; i++)
            {
                int length = random.Next(1, 200_000);
                random.NextBytes(span);
                using var handle = File.OpenHandle(Path.Combine(folder, $"{i:D5}.bin"), FileMode.Create, FileAccess.Write, FileShare.None, Win32.NoBuffering | FileOptions.WriteThrough);
                RandomAccess.Write(handle, span[..((length + 4095) / 4096 * 4096)], 0);
                RandomAccess.SetLength(handle, length);
            }
        }
        finally
        {
            System.Runtime.InteropServices.NativeMemory.AlignedFree(memory);
        }
    }

    [Fact]
    public void ReadsEveryFileExactly()
    {
        if (!IoRing.IsAvailable)
        {
            return;
        }

        var paths = MakeFiles(40, 3);
        paths.Insert(5, _temp.Combine("f/no-existe.bin"));
        var seen = new bool[paths.Count];
        IoRing.ReadAll(paths, 256 * 1024, unbuffered: true, (i, data, error) =>
        {
            seen[i] = true;
            if (i == 5)
            {
                Assert.NotNull(error);
                return;
            }

            Assert.True(error is null, $"{i} {new FileInfo(paths[i]).Length} {error?.Message}");
            Assert.True(data.SequenceEqual(File.ReadAllBytes(paths[i])), paths[i]);
        });
        Assert.All(seen, Assert.True);
    }

    [Fact]
    public void HashOperationGivesTheSameSumsWithAndWithoutIoRing()
    {
        MakeFiles(60, 9);
        string Run(bool ring)
        {
            var options = new CopyOptions { ProgressInterval = TimeSpan.Zero, Operation = JobOperation.Hash, HashAlgorithm = FileHashAlgorithm.Sha256, BatchedReads = ring };
            var engine = new CopyEngine(options);
            Assert.True(engine.Run(new CopyJobRequest([_temp.Combine("f")], _temp.Combine("f"))).Success);
            string text = File.ReadAllText(engine.ChecksumFile!);
            File.Delete(engine.ChecksumFile!);
            return text;
        }

        Assert.Equal(Run(false), Run(true));
    }

    [Fact]
    public void TestOperationStillFindsTheDamagedFile()
    {
        var paths = MakeFiles(150, 5);
        var hash = new CopyEngine(new CopyOptions { ProgressInterval = TimeSpan.Zero, Operation = JobOperation.Hash, HashAlgorithm = FileHashAlgorithm.XxHash3 });
        Assert.True(hash.Run(new CopyJobRequest([_temp.Combine("f")], _temp.Combine("f"))).Success);

        byte[] damaged = File.ReadAllBytes(paths[101]);
        damaged[damaged.Length / 2] ^= 0xFF;
        File.WriteAllBytes(paths[101], damaged);
        File.SetLastWriteTimeUtc(paths[101], File.GetLastWriteTimeUtc(paths[100]));

        var test = new CopyEngine(new CopyOptions { ProgressInterval = TimeSpan.Zero, Operation = JobOperation.Test });
        var result = test.Run(new CopyJobRequest([hash.ChecksumFile!], _temp.Path));
        var failure = Assert.Single(result.Failures);
        Assert.Equal(Path.GetFullPath(paths[101]), failure.Source);
        Assert.Equal(149, result.FilesCopied);
    }

    /// <summary>Medición (solo con MEGACOPIER_BENCH=ruta del informe): IoRing contra lecturas de una en una, ambas sin caché.</summary>
    [Fact]
    public void Benchmark()
    {
        string? report = Environment.GetEnvironmentVariable("MEGACOPIER_BENCH");
        if (report is null || !IoRing.IsAvailable)
        {
            return;
        }

        var paths = MakeFiles(3000, 1);
        byte[] buffer = GC.AllocateArray<byte>(256 * 1024 + 4096, pinned: true);
        long sink = 0;
        double Sequential()
        {
            var watch = Stopwatch.StartNew();
            foreach (string path in paths)
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, Win32.NoBuffering);
                unsafe
                {
                    fixed (byte* p = buffer)
                    {
                        nint aligned = ((nint)p + 4095) & ~4095;
                        sink += RandomAccess.Read(handle, new Span<byte>((void*)aligned, 256 * 1024), 0);
                    }
                }
            }

            return watch.Elapsed.TotalMilliseconds;
        }

        double Ring(int batch)
        {
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < paths.Count; i += batch)
            {
                IoRing.ReadAll(paths.GetRange(i, Math.Min(batch, paths.Count - i)), 256 * 1024, unbuffered: true, (_, data, error) => sink += error is null ? data.Length : throw error);
            }

            return watch.Elapsed.TotalMilliseconds;
        }

        var lines = new List<string>();
        for (int round = 0; round < 6; round++)
        {
            lines.Add(round % 2 == 0 ? $"secuencial {Sequential():N0} ms | ioring 64: {Ring(64):N0} ms" : $"ioring 64: {Ring(64):N0} ms | secuencial {Sequential():N0} ms");
        }

        foreach (var algorithm in new[] { FileHashAlgorithm.XxHash3, FileHashAlgorithm.Sha256 })
        {
            for (int round = 0; round < 6; round++)
            {
                // En frío: archivos nuevos escritos sin caché (nada de ellos está en memoria al leerlos).
                bool ring = round % 2 == 1;
                string folder = _temp.Combine($"frio{algorithm}{round}");
                WriteCold(folder, 2000, round);
                var engine = new CopyEngine(new CopyOptions { ProgressInterval = TimeSpan.Zero, Operation = JobOperation.Hash, HashAlgorithm = algorithm, BatchedReads = ring });
                var watch = Stopwatch.StartNew();
                engine.Run(new CopyJobRequest([folder], folder));
                lines.Add($"sumas en frío {algorithm} {(ring ? "ioring" : "normal")}: {watch.Elapsed.TotalMilliseconds:N0} ms");
                Directory.Delete(folder, recursive: true);
            }
        }

        File.WriteAllLines(report, lines);
        Assert.True(sink > 0);
    }
}
