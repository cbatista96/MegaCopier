using System.Security.Cryptography;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Tests;

public sealed class EfsRawTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>
    /// Cifrar un archivo de prueba solo si el usuario ya usa EFS: si no, Windows crearía un certificado nuevo en su
    /// almacén, y las pruebas no deben tocarlo.
    /// </summary>
    private static bool UserHasEfsKey()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\EFS\CurrentKeys");
        return key?.GetValue("CertificateHash") is not null;
    }

    [Fact]
    public void ImportErrorsAreReportedWithoutHanging()
    {
        // Windows solo exporta e importa datos realmente cifrados: con un archivo normal falla, y el error tiene que
        // llegar sin que la exportación (en otro hilo) ni la importación se queden esperando en la tubería.
        File.WriteAllBytes(_temp.Combine("a.bin"), RandomNumberGenerator.GetBytes(5_000_000));
        File.WriteAllBytes(_temp.Combine("existe.bin"), []);
        var copy = Task.Run(() => EfsRaw.Copy(_temp.Combine("a.bin"), _temp.Combine("existe.bin"), _ => { }));
        Assert.True(((IAsyncResult)copy).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)));
        Assert.True(copy.IsFaulted);
        Assert.True(copy.Exception!.InnerException is IOException or UnauthorizedAccessException);
    }

    [Fact]
    public void FilesThatAreNotEncryptedCopyNormally()
    {
        byte[] content = RandomNumberGenerator.GetBytes(2_000_000);
        File.WriteAllBytes(_temp.Combine("normal.bin"), content);
        var engine = new CopyEngine(new CopyOptions { EfsRaw = true, Verify = true });
        var result = engine.Run(new CopyJobRequest([_temp.Combine("normal.bin")], _temp.Combine("destino")));
        Assert.Empty(result.Failures);
        Assert.Equal(content, File.ReadAllBytes(_temp.Combine("destino", "normal.bin")));
    }

    [Fact]
    public void EncryptedFilesStayEncryptedAndVerify()
    {
        if (!UserHasEfsKey())
        {
            return; // sin clave EFS previa no se cifra nada (ver UserHasEfsKey)
        }

        byte[] content = RandomNumberGenerator.GetBytes(3_000_000);
        string source = _temp.Combine("origen", "cifrado.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, content);
        File.Encrypt(source);

        var engine = new CopyEngine(new CopyOptions { EfsRaw = true, Verify = true });
        var result = engine.Run(new CopyJobRequest([source], _temp.Combine("destino")));

        Assert.Empty(result.Failures);
        string copied = _temp.Combine("destino", "cifrado.bin");
        Assert.True(File.GetAttributes(copied).HasFlag(FileAttributes.Encrypted));
        Assert.Equal(content, File.ReadAllBytes(copied));
    }
}
