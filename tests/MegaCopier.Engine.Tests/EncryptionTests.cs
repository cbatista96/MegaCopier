using System.Text;

namespace MegaCopier.Engine.Tests;

public sealed class EncryptionTests : IDisposable
{
    private const string Password = "pendrive de las vacaciones";
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CopyOptions Encrypting() => new()
    {
        Encrypt = true,
        EncryptionPassword = Password,
        Verify = true,
        HashAlgorithm = FileHashAlgorithm.Sha256,
        ProgressInterval = TimeSpan.Zero,
    };

    private string Write(string relative, string text)
    {
        string path = _temp.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        return path;
    }

    private CopyJobResult Decrypt(string password, string from, string to) => new CopyEngine(new CopyOptions
    {
        Operation = JobOperation.Decrypt,
        EncryptionPassword = password,
        ProgressInterval = TimeSpan.Zero,
    }).Run(new CopyJobRequest([from], to));

    [Fact]
    public void EncryptedCopyHidesTheContentAndDecryptsBack()
    {
        string secret = "Número de cuenta: ES00 1234 5678 9999 " + new string('x', 3_000_000);
        Write("docs/banco.txt", secret);
        Write("docs/vacio.txt", "");

        var engine = new CopyEngine(Encrypting());
        var result = engine.Run(new CopyJobRequest([_temp.Combine("docs")], _temp.Combine("usb")));

        Assert.True(result.Success);
        Assert.All(engine.Files.Snapshot(), i => Assert.Equal(FileState.Verified, i.State));
        string encrypted = _temp.Combine(@"usb\docs\banco.txt.mcenc");
        Assert.Equal(["banco.txt.mcenc", "vacio.txt.mcenc"], Directory.GetFiles(_temp.Combine(@"usb\docs")).Select(Path.GetFileName).Order());
        Assert.DoesNotContain("ES00 1234", Encoding.UTF8.GetString(File.ReadAllBytes(encrypted)));
        Assert.Equal(EncryptedFile.EncryptedLength(Encoding.UTF8.GetByteCount(secret)), new FileInfo(encrypted).Length);

        var back = Decrypt(Password, _temp.Combine("usb"), _temp.Combine("vuelta"));

        Assert.True(back.Success);
        Assert.Equal(secret, File.ReadAllText(_temp.Combine(@"vuelta\usb\docs\banco.txt")));
        Assert.Equal("", File.ReadAllText(_temp.Combine(@"vuelta\usb\docs\vacio.txt")));
        Assert.Equal(new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc), File.GetLastWriteTimeUtc(_temp.Combine(@"vuelta\usb\docs\banco.txt")));

        // Repetir la copia: lo que ya está cifrado y no cambió no se vuelve a cifrar.
        var again = new CopyEngine(Encrypting()).Run(new CopyJobRequest([_temp.Combine("docs")], _temp.Combine("usb")));
        Assert.Equal(2, again.FilesSkipped);
    }

    [Fact]
    public void WrongPasswordTamperingAndTruncationAreDetected()
    {
        Write("a.txt", new string('a', 2_500_000));
        new CopyEngine(Encrypting()).Run(new CopyJobRequest([_temp.Combine("a.txt")], _temp.Combine("usb")));
        string encrypted = _temp.Combine(@"usb\a.txt.mcenc");

        var wrong = Decrypt("otra contraseña", encrypted, _temp.Combine("mal"));
        Assert.Equal(FileErrorKind.ChecksumMismatch, Assert.Single(wrong.Failures).Kind);
        Assert.False(File.Exists(_temp.Combine(@"mal\a.txt")));

        byte[] data = File.ReadAllBytes(encrypted);
        data[1_500_000] ^= 1; // un bit cambiado en el segundo bloque
        File.WriteAllBytes(_temp.Combine(@"tocado\a.txt.mcenc").Also(p => Directory.CreateDirectory(Path.GetDirectoryName(p)!)), data);
        Assert.Single(Decrypt(Password, _temp.Combine("tocado"), _temp.Combine("t1")).Failures);

        data[1_500_000] ^= 1;
        File.WriteAllBytes(_temp.Combine(@"cortado\a.txt.mcenc").Also(p => Directory.CreateDirectory(Path.GetDirectoryName(p)!)), data[..(1024 * 1024 + 16 + 44)]);
        Assert.Single(Decrypt(Password, _temp.Combine("cortado"), _temp.Combine("t2")).Failures); // le falta el último bloque
    }

    [Fact]
    public void MovingWithEncryptionNeverLeavesPlainFiles()
    {
        string source = Write("src/clave.txt", "secreto");
        var options = Encrypting();
        options.Move = true;

        var result = new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(_temp.Combine(@"dst\src\clave.txt")));
        Assert.True(File.Exists(_temp.Combine(@"dst\src\clave.txt.mcenc")));
    }

    [Fact]
    public void EncryptedJobWithoutPasswordCopiesNothing()
    {
        Write("src/clave.txt", "secreto");
        var options = Encrypting();
        options.EncryptionPassword = ""; // tras reiniciar: la contraseña no se guarda

        Assert.Throws<CopyPlanException>(() => new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst"))));
        Assert.False(Directory.Exists(_temp.Combine("dst")));
    }
}

internal static class TestExtensions
{
    public static string Also(this string value, Action<string> action)
    {
        action(value);
        return value;
    }
}
