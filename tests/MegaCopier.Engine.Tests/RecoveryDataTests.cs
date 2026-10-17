using System.Security.Cryptography;
using MegaCopier.Engine.IO;

namespace MegaCopier.Engine.Tests;

public sealed class RecoveryDataTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private (string File, string Recovery, byte[] Original) Make(int size, int percent = 10)
    {
        byte[] original = RandomNumberGenerator.GetBytes(size);
        string file = _temp.Combine("copia.bin");
        File.WriteAllBytes(file, original);
        string recovery = RecoveryData.PathFor(_temp.Path, file);
        RecoveryData.Create(file, recovery, percent);
        return (file, recovery, original);
    }

    private static void Damage(string file, long offset, int count)
    {
        var lastWrite = File.GetLastWriteTimeUtc(file);
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = offset;
            stream.Write(RandomNumberGenerator.GetBytes(count));
        }

        File.SetLastWriteTimeUtc(file, lastWrite); // corrupción silenciosa: la fecha no cambia
    }

    [Fact]
    public void ScatteredAndBurstDamageIsRepaired()
    {
        var (file, recovery, original) = Make(30_000_000); // 3 tramos
        Damage(file, 100, 10);                     // un bloque
        Damage(file, 5_000_000, 70_000);           // dos bloques seguidos
        Damage(file, 14_000_000, 1_200_000);       // ráfaga de ~19 bloques en el segundo tramo
        Damage(file, original.Length - 50, 50);    // el final (bloque incompleto)

        var (outcome, blocks) = RecoveryData.Repair(file, recovery);

        Assert.Equal(RepairOutcome.Repaired, outcome);
        Assert.True(blocks >= 22);
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Equal((RepairOutcome.Intact, 0), RecoveryData.Repair(file, recovery));
    }

    [Fact]
    public void TooMuchDamageIsReportedAndAFileEditedLaterIsLeftAlone()
    {
        var (file, recovery, _) = Make(3_000_000, percent: 5); // 46 bloques, 3 de paridad
        Damage(file, 0, 300_000);                              // 5 bloques
        Assert.Equal(RepairOutcome.Unrepairable, RecoveryData.Repair(file, recovery).Outcome);

        File.AppendAllText(file, "editado");
        Assert.Equal(RepairOutcome.Changed, RecoveryData.Repair(file, recovery).Outcome);
    }

    [Fact]
    public void SmallFilesAndRecoverySizeAreSensible()
    {
        var (file, recovery, original) = Make(1_000);
        Damage(file, 10, 5);
        Assert.Equal(RepairOutcome.Repaired, RecoveryData.Repair(file, recovery).Outcome);
        Assert.Equal(original, File.ReadAllBytes(file));

        var big = Make(13_107_200); // 200 bloques exactos al 10 %: 20 de paridad
        Assert.InRange(new FileInfo(big.Recovery).Length, 1_300_000, 1_350_000);
    }

    [Fact]
    public void ReedSolomonRecoversAnyCombination()
    {
        var rs = new ReedSolomon(10, 4);
        var data = Enumerable.Range(0, 10).Select(_ => RandomNumberGenerator.GetBytes(64)).ToList();
        var parity = Enumerable.Range(0, 4).Select(_ => new byte[64]).ToList();
        rs.Encode(data, parity, 64);
        foreach (int[] lost in new[] { new[] { 0 }, new[] { 9, 3 }, new[] { 1, 2, 3, 4 } })
        {
            var broken = data.Select((d, i) => lost.Contains(i) ? null : (byte[]?)d.ToArray()).ToArray();
            var spare = parity.Select((p, j) => j == 0 && lost.Length < 4 ? null : (byte[]?)p).ToArray(); // a veces falta también paridad
            Assert.True(rs.Reconstruct(broken, spare, 64));
            Assert.All(Enumerable.Range(0, 10), i => Assert.Equal(data[i], broken[i]));
        }
    }

    [Fact]
    public void CopyWithRecoveryDataThenRepairTheDamagedBackup()
    {
        string source = _temp.Combine("origen");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        byte[] big = RandomNumberGenerator.GetBytes(6_000_000);
        File.WriteAllBytes(Path.Combine(source, "sub", "grande.bin"), big);
        File.WriteAllText(Path.Combine(source, "nota.txt"), "nota importante");
        string backup = _temp.Combine("copia");

        var copy = new CopyEngine(new CopyOptions { RecoveryPercent = 10 }).Run(new CopyJobRequest([source], backup));
        Assert.Empty(copy.Failures);
        var folder = new DirectoryInfo(Path.Combine(backup, RecoveryData.Folder));
        Assert.True(folder.Attributes.HasFlag(FileAttributes.Hidden));
        Assert.True(File.Exists(Path.Combine(backup, RecoveryData.Folder, "origen", "sub", "grande.bin" + RecoveryData.Extension)));

        string damaged = Path.Combine(backup, "origen", "sub", "grande.bin");
        Damage(damaged, 1_000_000, 200_000);

        // Un espejo sobre la copia no se lleva los datos de recuperación.
        new CopyEngine(new CopyOptions { Mode = CopyMode.Mirror, Conflict = ConflictPolicy.Skip }).Run(new CopyJobRequest([source], backup));
        Assert.True(folder.Exists);

        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Repair });
        var repair = engine.Run(new CopyJobRequest([Path.Combine(backup, "origen")], backup));

        Assert.Empty(repair.Failures);
        Assert.Equal(1, repair.FilesCopied);   // reparado
        Assert.Equal(1, repair.FilesSkipped);  // la nota estaba bien
        Assert.Equal(big, File.ReadAllBytes(damaged));
        Assert.Throws<CopyPlanException>(() => new CopyEngine(new CopyOptions { Operation = JobOperation.Repair }).Run(new CopyJobRequest([source], source)));
    }
}
