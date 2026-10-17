using System.Security.Cryptography;

namespace MegaCopier.Engine.Tests;

public sealed class MultiSourceTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string? MultiSourceLogged { get; set; }

    private byte[] TwoCopies(int size, string name = "video.mkv")
    {
        byte[] data = RandomNumberGenerator.GetBytes(size);
        var when = DateTime.UtcNow.AddDays(-3);
        foreach (string folder in new[] { "nas1", "nas2", "nas3" })
        {
            Directory.CreateDirectory(_temp.Combine(folder, "pelis"));
            File.WriteAllBytes(_temp.Combine(folder, "pelis", name), data);
            File.SetLastWriteTimeUtc(_temp.Combine(folder, "pelis", name), when);
        }

        return data;
    }

    private CopyJobResult Copy(bool verify, params string[] alternates)
    {
        var options = new CopyOptions { Verify = verify, AlternateSources = [.. alternates] };
        var engine = new CopyEngine(options);
        engine.Logged += entry => { if (entry.Message.Contains(Texts.F("E_MultiSourceUsed", 3)[..10], StringComparison.Ordinal)) { MultiSourceLogged = entry.Message; } };
        return engine.Run(new CopyJobRequest([_temp.Combine("nas1", "pelis")], _temp.Combine("destino")));
    }

    [Fact]
    public void ABigFileIsReadFromSeveralPlacesAndArrivesIntact()
    {
        byte[] data = TwoCopies(150_000_000);
        var result = Copy(verify: true, _temp.Combine("nas2", "pelis"), _temp.Combine("nas3", "pelis"), _temp.Combine("no-existe"));
        Assert.Empty(result.Failures);
        Assert.Equal(Texts.F("E_MultiSourceUsed", 3), MultiSourceLogged); // el original y las dos copias que existen
        Assert.Equal(data, File.ReadAllBytes(_temp.Combine("destino", "pelis", "video.mkv")));
        Assert.Equal(File.GetLastWriteTimeUtc(_temp.Combine("nas1", "pelis", "video.mkv")), File.GetLastWriteTimeUtc(_temp.Combine("destino", "pelis", "video.mkv")));
    }

    [Fact]
    public void ADifferentAlternateIsNeverMixedIn()
    {
        byte[] data = TwoCopies(100_000_000);
        // nas2 cambió por dentro, pero conserva tamaño y fecha (y los extremos): solo la verificación lo detecta.
        string tampered = _temp.Combine("nas2", "pelis", "video.mkv");
        var when = File.GetLastWriteTimeUtc(tampered);
        using (var stream = new FileStream(tampered, FileMode.Open, FileAccess.Write))
        {
            stream.Position = 30_000_000;
            stream.Write(RandomNumberGenerator.GetBytes(1_000_000));
        }

        File.SetLastWriteTimeUtc(tampered, when);
        var result = Copy(verify: true, _temp.Combine("nas2", "pelis"));
        Assert.Empty(result.Failures);
        Assert.Equal(data, File.ReadAllBytes(_temp.Combine("destino", "pelis", "video.mkv")));

        // Y si cambian los extremos, ni siquiera se usa.
        using (var stream = new FileStream(tampered, FileMode.Open, FileAccess.Write))
        {
            stream.Write(RandomNumberGenerator.GetBytes(10));
        }

        File.SetLastWriteTimeUtc(tampered, when);
        File.Delete(_temp.Combine("destino", "pelis", "video.mkv"));
        Assert.Empty(Copy(verify: false, _temp.Combine("nas2", "pelis")).Failures);
        Assert.Equal(data, File.ReadAllBytes(_temp.Combine("destino", "pelis", "video.mkv")));
    }
}
