using System.Security.Cryptography;
using System.Text;

namespace MegaCopier.Engine.Tests;

public sealed class RansomwareShieldTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static byte[] Text(int i) => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"Informe trimestral {i}: ventas, gastos y notas. ", 400)));

    /// <summary>Copia de seguridad hecha ayer; hoy el origen tiene los mismos nombres con otro contenido.</summary>
    private void Prepare(int files, Func<int, byte[]> today, string extension = ".docx")
    {
        for (int i = 0; i < files; i++)
        {
            string name = $"doc{i}{extension}";
            Directory.CreateDirectory(_temp.Combine("destino", "origen"));
            File.WriteAllBytes(_temp.Combine("destino", "origen", name), Text(i));
            File.SetLastWriteTimeUtc(_temp.Combine("destino", "origen", name), DateTime.UtcNow.AddDays(-1));
            Directory.CreateDirectory(_temp.Combine("origen"));
            File.WriteAllBytes(_temp.Combine("origen", name), today(i));
        }
    }

    [Fact]
    public void EncryptedLookingReplacementsStopTheCopyAndTouchNothing()
    {
        Prepare(20, _ => RandomNumberGenerator.GetBytes(30_000));
        var engine = new CopyEngine(new CopyOptions { Conflict = ConflictPolicy.Overwrite });
        var error = Assert.Throws<CopyPlanException>(() => engine.Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino"))));
        Assert.Contains("20", error.Message);
        Assert.Equal(Text(0), File.ReadAllBytes(_temp.Combine("destino", "origen", "doc0.docx"))); // la copia buena sigue intacta
    }

    [Fact]
    public void NormalEditsAndTurningTheShieldOffCopyAsUsual()
    {
        Prepare(20, i => Text(i + 100));
        Assert.Empty(new CopyEngine(new CopyOptions { Conflict = ConflictPolicy.Overwrite }).Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino"))).Failures);

        using var other = new TempDir();
        var random = Enumerable.Range(0, 20).Select(_ => RandomNumberGenerator.GetBytes(30_000)).ToList();
        foreach (int i in Enumerable.Range(0, 20))
        {
            File.WriteAllBytes(_temp.Combine("origen", $"doc{i}.docx"), random[i]);
        }

        var result = new CopyEngine(new CopyOptions { Conflict = ConflictPolicy.Overwrite, RansomwareShield = false }).Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino")));
        Assert.Empty(result.Failures);
        Assert.Equal(random[0], File.ReadAllBytes(_temp.Combine("destino", "origen", "doc0.docx")));
    }

    [Fact]
    public void AFewChangedPhotosDoNotTriggerIt()
    {
        // Lo que ya era aleatorio (fotos, ZIP...) no cuenta, y unos pocos archivos tampoco.
        for (int i = 0; i < 30; i++)
        {
            Directory.CreateDirectory(_temp.Combine("destino", "origen"));
            Directory.CreateDirectory(_temp.Combine("origen"));
            File.WriteAllBytes(_temp.Combine("destino", "origen", $"foto{i}.jpg"), RandomNumberGenerator.GetBytes(20_000));
            File.WriteAllBytes(_temp.Combine("origen", $"foto{i}.jpg"), RandomNumberGenerator.GetBytes(21_000));
        }

        Prepare(3, _ => RandomNumberGenerator.GetBytes(30_000));
        Assert.Empty(new CopyEngine(new CopyOptions { Conflict = ConflictPolicy.Overwrite }).Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino"))).Failures);
    }

    [Fact]
    public void SyncIsShieldedToo()
    {
        Directory.CreateDirectory(_temp.Combine("izq"));
        Directory.CreateDirectory(_temp.Combine("der"));
        for (int i = 0; i < 12; i++)
        {
            File.WriteAllBytes(_temp.Combine("izq", $"n{i}.txt"), Text(i));
            File.WriteAllBytes(_temp.Combine("der", $"n{i}.txt"), Text(i));
        }

        CopyEngine Engine() => new(new CopyOptions { Operation = JobOperation.Sync, SyncDirection = SyncDirection.LeftToRight }) { SyncStateFolder = _temp.Combine("estado"), SyncUseRecycleBin = false };
        Engine().Run(new CopyJobRequest([_temp.Combine("izq")], _temp.Combine("der")));
        for (int i = 0; i < 12; i++)
        {
            File.WriteAllBytes(_temp.Combine("izq", $"n{i}.txt"), RandomNumberGenerator.GetBytes(25_000));
        }

        Assert.Throws<CopyPlanException>(() => Engine().Run(new CopyJobRequest([_temp.Combine("izq")], _temp.Combine("der"))));
        Assert.Equal(Text(0), File.ReadAllBytes(_temp.Combine("der", "n0.txt")));
    }

    [Fact]
    public void EntropyOfRandomAndText()
    {
        Assert.True(RansomwareShield.Entropy(RandomNumberGenerator.GetBytes(65536)) > 7.9);
        Assert.True(RansomwareShield.Entropy(Text(1)) < 5);
    }
}
