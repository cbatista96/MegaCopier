using System.Security.Cryptography;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Tests;

public sealed class BackupModeTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static bool IsAdmin => new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    [Fact]
    public void WithoutAdministratorRightsItSaysSoInsteadOfFailingEveryFile()
    {
        if (IsAdmin)
        {
            return; // con permisos de administrador sí se activan los privilegios
        }

        File.WriteAllText(_temp.Combine("a.txt"), "a");
        var engine = new CopyEngine(new CopyOptions { BackupMode = true });
        var error = Assert.Throws<CopyPlanException>(() => engine.Run(new CopyJobRequest([_temp.Combine("a.txt")], _temp.Combine("destino"))));
        Assert.Contains("administrador", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FilesOpenedWithBackupSemanticsCopyTheSame(bool unbuffered)
    {
        // Lo que cambia en modo copia de seguridad es cómo se abren los archivos (CreateFileW con
        // FILE_FLAG_BACKUP_SEMANTICS): la copia normal, la de archivos pequeños y la verificación tienen que ir igual.
        byte[] big = RandomNumberGenerator.GetBytes(9_000_000);
        Directory.CreateDirectory(_temp.Combine("origen", "sub"));
        File.WriteAllBytes(_temp.Combine("origen", "grande.bin"), big);
        File.WriteAllText(_temp.Combine("origen", "sub", "pequeño.txt"), "hola");
        var engine = new CopyEngine(new CopyOptions { BackupMode = true, Verify = true, UseUnbufferedIo = unbuffered, ProgressInterval = TimeSpan.Zero })
        {
            SkipBackupPrivilegeCheck = true,
        };

        var result = engine.Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino")));

        Assert.Empty(result.Failures);
        Assert.Equal(big, File.ReadAllBytes(_temp.Combine("destino", "origen", "grande.bin")));
        Assert.Equal("hola", File.ReadAllText(_temp.Combine("destino", "origen", "sub", "pequeño.txt")));
    }

    [Fact]
    public void BackupOpenHandlesLongAndMissingPaths()
    {
        string deep = _temp.Combine(string.Join('\\', Enumerable.Repeat(new string('x', 50), 6)));
        Directory.CreateDirectory(deep);
        string file = Path.Combine(deep, "largo.txt");
        File.WriteAllText(file, "ruta de más de 260 caracteres");
        Assert.True(file.Length > 260);
        using (var handle = BackupPrivileges.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None))
        {
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount("ruta de más de 260 caracteres"), RandomAccess.GetLength(handle));
        }

        Assert.ThrowsAny<IOException>(() => BackupPrivileges.Open(_temp.Combine("no-existe.txt"), FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None));
    }
}
