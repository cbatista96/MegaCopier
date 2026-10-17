using System.Security.Cryptography;
using MegaCopier.Engine.Forensics;

namespace MegaCopier.Engine.Tests;

[Collection("Forensic")] // la carpeta de la clave es estática
public sealed class ForensicTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _keyFolder;

    public ForensicTests()
    {
        _keyFolder = ForensicManifest.KeyFolder;
        ForensicManifest.KeyFolder = _temp.Combine("clave"); // nunca la del usuario
    }

    public void Dispose()
    {
        ForensicManifest.KeyFolder = _keyFolder;
        _temp.Dispose();
    }

    private string CopyWithManifest()
    {
        Directory.CreateDirectory(_temp.Combine("pruebas", "fotos"));
        File.WriteAllBytes(_temp.Combine("pruebas", "fotos", "imagen.jpg"), RandomNumberGenerator.GetBytes(800_000));
        File.WriteAllText(_temp.Combine("pruebas", "acta.txt"), "acta de entrega");
        var engine = new CopyEngine(new CopyOptions { Forensic = true });
        var result = engine.Run(new CopyJobRequest([_temp.Combine("pruebas")], _temp.Combine("copia")));
        Assert.Empty(result.Failures);
        Assert.NotNull(engine.LastManifest);
        return engine.LastManifest!;
    }

    [Fact]
    public void AnUntouchedCopyChecksOut()
    {
        string manifest = CopyWithManifest();
        Assert.True(File.Exists(manifest + ForensicManifest.SignatureExtension));
        var check = ForensicManifest.Verify(manifest);
        Assert.True(check.AllGood);
        Assert.Equal(2, check.Files);
        Assert.Equal(16, check.KeyFingerprint.Length);
        Assert.Contains("\"sha256Copy\"", File.ReadAllText(manifest));
    }

    [Fact]
    public void ChangedMissingFilesAndAForgedManifestAreDetected()
    {
        string manifest = CopyWithManifest();
        File.AppendAllText(_temp.Combine("copia", "pruebas", "acta.txt"), " (añadido después)");
        File.Delete(_temp.Combine("copia", "pruebas", "fotos", "imagen.jpg"));
        var check = ForensicManifest.Verify(manifest);
        Assert.True(check.SignatureValid);
        Assert.Equal([Path.Combine("pruebas", "acta.txt")], check.Changed);
        Assert.Single(check.Missing);

        // Quien edite el manifiesto (por ejemplo, para "arreglar" una suma) rompe la firma.
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("acta.txt", "acta2.txt"));
        Assert.False(ForensicManifest.Verify(manifest).SignatureValid);
    }

    [Fact]
    public void TheKeyIsReusedAndProtected()
    {
        CopyWithManifest();
        byte[] stored = File.ReadAllBytes(Path.Combine(ForensicManifest.KeyFolder, "forensic-key.bin"));
        using var key = ForensicManifest.LoadOrCreateKey();
        using var again = ForensicManifest.LoadOrCreateKey();
        Assert.Equal(key.ExportSubjectPublicKeyInfo(), again.ExportSubjectPublicKeyInfo());
        Assert.Throws<CryptographicException>(() => ECDsa.Create().ImportPkcs8PrivateKey(stored, out _)); // cifrada con DPAPI, no en claro
    }
}
