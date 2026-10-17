using System.Security.Cryptography;
using MegaCopier.Engine.Backup;

namespace MegaCopier.Engine.Tests;

public sealed class SnapshotTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Source => _temp.Combine("datos");

    private string Repo => _temp.Combine("repo");

    private static long FolderSize(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    private CopyEngine Backup(int daily = 0)
    {
        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Snapshot, VersionsDaily = daily });
        Assert.Empty(engine.Run(new CopyJobRequest([Source], Repo)).Failures);
        return engine;
    }

    private void Restore(string id, string target)
    {
        var result = new CopyEngine(new CopyOptions { Operation = JobOperation.Restore, SnapshotId = id }).Run(new CopyJobRequest([Repo], target));
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void TenBackupsOfAlmostTheSameDataTakeLittleMoreThanOne()
    {
        Directory.CreateDirectory(Path.Combine(Source, "vacía"));
        byte[] video = RandomNumberGenerator.GetBytes(20_000_000);
        File.WriteAllBytes(Path.Combine(Source, "video.mp4"), video);
        File.WriteAllBytes(Path.Combine(Source, "copia-del-video.mp4"), video); // duplicado dentro de la misma copia
        File.WriteAllText(Path.Combine(Source, "notas.txt"), "v1");

        var first = Backup();
        long afterFirst = FolderSize(Repo);
        Assert.InRange(afterFirst, 19_000_000, 21_500_000); // el vídeo repetido se guarda una vez
        string firstId = first.LastSnapshot!.Id;

        var snapshots = new List<string> { firstId };
        for (int i = 2; i <= 10; i++)
        {
            Thread.Sleep(1100); // otro segundo: otra instantánea
            File.WriteAllText(Path.Combine(Source, "notas.txt"), $"versión {i}");
            File.SetLastWriteTimeUtc(Path.Combine(Source, "notas.txt"), DateTime.UtcNow.AddMinutes(i));
            snapshots.Add(Backup().LastSnapshot!.Id);
        }

        Assert.True(FolderSize(Repo) < afterFirst + 200_000);
        Assert.Equal(10, new BackupRepository(Repo).Snapshots().Count);

        // Volver a la primera.
        Restore(firstId, _temp.Combine("vuelta"));
        Assert.Equal("v1", File.ReadAllText(_temp.Combine("vuelta", "datos", "notas.txt")));
        Assert.Equal(video, File.ReadAllBytes(_temp.Combine("vuelta", "datos", "video.mp4")));
        Assert.True(Directory.Exists(_temp.Combine("vuelta", "datos", "vacía")));
        Restore("latest", _temp.Combine("ultima"));
        Assert.Equal("versión 10", File.ReadAllText(_temp.Combine("ultima", "datos", "notas.txt")));
    }

    [Fact]
    public void InsertingAtTheStartOfABigFileStoresOnlyAFewNewPieces()
    {
        Directory.CreateDirectory(Source);
        byte[] original = RandomNumberGenerator.GetBytes(30_000_000);
        File.WriteAllBytes(Path.Combine(Source, "grande.bin"), original);
        Backup();
        long before = FolderSize(Repo);

        File.WriteAllBytes(Path.Combine(Source, "grande.bin"), [.. RandomNumberGenerator.GetBytes(1000), .. original]);
        Thread.Sleep(1100);
        Backup();

        Assert.True(FolderSize(Repo) - before < 6_000_000, $"{FolderSize(Repo) - before} bytes nuevos"); // los cortes por contenido se recolocan
    }

    [Fact]
    public void PruningKeepsTheNewestAndRemovesUnusedPieces()
    {
        Directory.CreateDirectory(Source);
        for (int i = 0; i < 3; i++)
        {
            File.WriteAllBytes(Path.Combine(Source, "cambia.bin"), RandomNumberGenerator.GetBytes(2_000_000));
            Backup();
            Thread.Sleep(1100);
        }

        Backup(daily: 1); // hoy: solo se queda la más reciente
        var repository = new BackupRepository(Repo);
        Assert.Single(repository.Snapshots());
        Assert.InRange(FolderSize(Path.Combine(Repo, "chunks")), 1_900_000, 2_200_000);
    }

    [Fact]
    public void ADamagedPieceIsDetectedOnRestore()
    {
        Directory.CreateDirectory(Source);
        File.WriteAllBytes(Path.Combine(Source, "a.bin"), RandomNumberGenerator.GetBytes(500_000));
        Backup();
        string chunk = Directory.EnumerateFiles(Path.Combine(Repo, "chunks"), "*", SearchOption.AllDirectories).First();
        byte[] bytes = File.ReadAllBytes(chunk);
        bytes[^10] ^= 0xFF;
        File.WriteAllBytes(chunk, bytes);

        var result = new CopyEngine(new CopyOptions { Operation = JobOperation.Restore }).Run(new CopyJobRequest([Repo], _temp.Combine("vuelta")));
        Assert.Single(result.Failures);
        Assert.False(File.Exists(_temp.Combine("vuelta", "datos", "a.bin")));
    }

    [Fact]
    public void ChunkBoundariesDependOnContent()
    {
        byte[] data = RandomNumberGenerator.GetBytes(12_000_000);
        var first = ContentChunker.Split(new MemoryStream(data)).Select(c => c.Count).ToList();
        Assert.Equal(data.Length, first.Sum());
        Assert.All(first.SkipLast(1), size => Assert.InRange(size, ContentChunker.MinSize, ContentChunker.MaxSize));
        var shifted = ContentChunker.Split(new MemoryStream([.. new byte[100], .. data])).Select(c => c.Count).ToList();
        Assert.Equal(first.Skip(2), shifted.Skip(2)); // tras el primer corte, todo coincide
    }
}
