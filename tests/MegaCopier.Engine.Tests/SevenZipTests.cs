using System.Security.Cryptography;
using MegaCopier.Engine.IO;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace MegaCopier.Engine.Tests;

/// <summary>Lo que escribe <see cref="SevenZipWriter"/> lo abre otro programa (SharpCompress), con y sin contraseña.</summary>
public sealed class SevenZipTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private readonly Dictionary<string, byte[]> _files = new()
    {
        [@"fotos\playa.jpg"] = RandomNumberGenerator.GetBytes(1_200_000),
        [@"textos\carta ñandú.txt"] = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("hola mundo ", 100_000))),
        ["grande.bin"] = RandomNumberGenerator.GetBytes(3_000_000),
        ["vacío.txt"] = [],
    };

    private string Write(string? password)
    {
        string path = _temp.Combine(password is null ? "plano.7z" : "cifrado.7z");
        using var output = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        var writer = new SevenZipWriter(output, password);
        writer.AddDirectory("fotos", DateTime.UtcNow);
        writer.AddDirectory("textos", DateTime.UtcNow);
        writer.AddDirectory("carpeta vacía", DateTime.UtcNow);
        foreach (var (name, content) in _files)
        {
            writer.AddFile(name, new MemoryStream(content), new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc), _ => { }, CancellationToken.None);
        }

        writer.Finish();
        return path;
    }

    private static Dictionary<string, byte[]> ReadAll(string path, string? password)
    {
        var options = new ReaderOptions { Password = password };
        using var archive = SevenZipArchive.OpenArchive(path, options);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                result[entry.Key!.Replace('/', '\\') + @"\"] = [];
                continue;
            }

            using var stream = entry.OpenEntryStream();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            result[entry.Key!.Replace('/', '\\')] = copy.ToArray();

        }

        return result;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("contraseña segura")]
    public void AnotherProgramOpensIt(string? password)
    {
        var read = ReadAll(Write(password), password);
        foreach (var (name, content) in _files)
        {
            Assert.True(content.AsSpan().SequenceEqual(read[name]), $"{name}: {content.Length} vs {read[name].Length}, primero distinto {content.AsSpan().CommonPrefixLength(read[name])}");
        }

        Assert.Contains(@"carpeta vacía\", read.Keys);
    }

    [Fact]
    public void WithAPasswordNamesAreHiddenAndAWrongOneFails()
    {
        string path = Write("clave buena");
        byte[] raw = File.ReadAllBytes(path);
        byte[] name = System.Text.Encoding.Unicode.GetBytes("playa.jpg");
        Assert.True(raw.AsSpan().IndexOf(name) < 0); // el nombre no aparece en claro
        Assert.ThrowsAny<Exception>(() => ReadAll(path, "clave mala"));
    }

    [Fact]
    public void CopyIntoAnEncrypted7zVerifiesAndAnotherProgramOpensIt()
    {
        foreach (var (name, content) in _files)
        {
            string path = _temp.Combine("origen", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        var engine = new CopyEngine(new CopyOptions { Zip = true, ArchiveFormat = ArchiveFormat.SevenZip, Encrypt = true, EncryptionPassword = "clave-123", Verify = true });
        var result = engine.Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino")));

        Assert.Empty(result.Failures);
        Assert.Equal(_temp.Combine("destino", "origen.7z"), engine.ZipFile);
        Assert.Equal(_files.Count, result.FilesCopied);
        var read = ReadAll(engine.ZipFile!, "clave-123");
        foreach (var (name, content) in _files)
        {
            Assert.Equal(content, read[@"origen\" + name]);
        }
    }
}
