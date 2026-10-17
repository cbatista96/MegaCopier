using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using MegaCopier.Engine.IO;

namespace MegaCopier.Engine.Tests;

public sealed class ZipCopyTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private Dictionary<string, byte[]> MakeSource()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["fotos/año 2026/playa.jpg"] = RandomNumberGenerator.GetBytes(1_500_000), // se guarda sin comprimir
            ["textos/carta ñandú.txt"] = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("hola mundo ", 200_000))),
            ["vacío.bin"] = [],
            ["grande.bin"] = RandomNumberGenerator.GetBytes(5_000_000),
        };
        foreach (var (name, content) in files)
        {
            string path = _temp.Combine("origen", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        Directory.CreateDirectory(_temp.Combine("origen", "carpeta vacía"));
        return files;
    }

    [Fact]
    public void PlainZipOpensWithTheZipReaderOfDotNet()
    {
        var files = MakeSource();
        var engine = new CopyEngine(new CopyOptions { Zip = true, Verify = true });
        var result = engine.Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino")));

        Assert.Empty(result.Failures);
        Assert.Equal(_temp.Combine("destino", "origen.zip"), engine.ZipFile);
        Assert.False(File.Exists(engine.ZipFile + ".mcpart"));
        using var zip = ZipFile.OpenRead(engine.ZipFile!);
        foreach (var (name, content) in files)
        {
            var entry = zip.GetEntry("origen/" + name);
            Assert.NotNull(entry);
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.Equal(content, copy.ToArray());
        }

        Assert.NotNull(zip.GetEntry("origen/carpeta vacía/"));
        // Lo ya comprimido no se vuelve a comprimir; el texto repetido, sí.
        Assert.Equal(1_500_000, zip.GetEntry("origen/fotos/año 2026/playa.jpg")!.CompressedLength);
        Assert.True(zip.GetEntry("origen/textos/carta ñandú.txt")!.CompressedLength < 100_000);
    }

    [Fact]
    public void EncryptedZipRoundTripsAndRejectsWrongPasswordsAndTampering()
    {
        var files = MakeSource();
        var engine = new CopyEngine(new CopyOptions { Zip = true, Encrypt = true, EncryptionPassword = "contraseña segura", Verify = true });
        var result = engine.Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino", "copia.zip")));
        Assert.Empty(result.Failures);
        Assert.Equal(_temp.Combine("destino", "copia.zip"), engine.ZipFile);

        byte[] Read(string password, string name)
        {
            using var zip = File.OpenRead(engine.ZipFile!);
            var entry = ZipArchiveReader.Entries(zip).Single(e => e.Name == name);
            Assert.True(entry.Encrypted);
            using var output = new MemoryStream();
            ZipArchiveReader.Extract(zip, entry, password, block => output.Write(block));
            return output.ToArray();
        }

        foreach (var (name, content) in files)
        {
            Assert.Equal(content, Read("contraseña segura", "origen/" + name));
        }

        Assert.Throws<CryptographicException>(() => Read("otra", "origen/grande.bin"));

        // Un byte cambiado dentro de lo cifrado: lo detecta el código de autenticación.
        using (var zip = new FileStream(engine.ZipFile!, FileMode.Open, FileAccess.ReadWrite))
        {
            var entry = ZipArchiveReader.Entries(zip).Single(e => e.Name == "origen/grande.bin");
            zip.Position = entry.Offset + 30 + "origen/grande.bin".Length + 20 + 11 + 18 + 1000;
            int value = zip.ReadByte();
            zip.Position--;
            zip.WriteByte((byte)(value ^ 0x40));
        }

        Assert.ThrowsAny<Exception>(() => Read("contraseña segura", "origen/grande.bin"));
    }

    [Fact]
    public void EncryptedZipOpensWithWindowsTar()
    {
        // tar.exe de Windows (libarchive) lee el AES de WinZip: es una comprobación independiente del formato.
        string tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tar))
        {
            return;
        }

        var files = MakeSource();
        var engine = new CopyEngine(new CopyOptions { Zip = true, Encrypt = true, EncryptionPassword = "clave-123" });
        Assert.Empty(engine.Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino"))).Failures);

        Directory.CreateDirectory(_temp.Combine("extraido"));
        var info = new ProcessStartInfo(tar) { WorkingDirectory = _temp.Combine("extraido"), RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-xf", engine.ZipFile!, "--passphrase", "clave-123" })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
        foreach (var (name, content) in files)
        {
            Assert.Equal(content, File.ReadAllBytes(_temp.Combine("extraido", "origen", name)));
        }
    }

    [Fact]
    public void MovingIntoAZipRemovesOnlyWhatWentIn()
    {
        MakeSource();
        var engine = new CopyEngine(new CopyOptions { Zip = true, Move = true, Verify = true });
        var result = engine.Run(new CopyJobRequest([_temp.Combine("origen")], _temp.Combine("destino")));
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(_temp.Combine("origen")));
        using var zip = ZipFile.OpenRead(engine.ZipFile!);
        Assert.Equal(4, zip.Entries.Count(e => !e.FullName.EndsWith('/')));
    }

    [Fact]
    public void AnExistingZipIsNotOverwrittenUnlessAsked()
    {
        File.WriteAllText(_temp.Combine("a.txt"), "a");
        Directory.CreateDirectory(_temp.Combine("destino"));
        File.WriteAllText(_temp.Combine("destino", "a.zip"), "no es mío");
        var engine = new CopyEngine(new CopyOptions { Zip = true, Conflict = ConflictPolicy.Skip });
        engine.Run(new CopyJobRequest([_temp.Combine("a.txt")], _temp.Combine("destino")));
        Assert.Equal(_temp.Combine("destino", "a (2).zip"), engine.ZipFile);
        Assert.Equal("no es mío", File.ReadAllText(_temp.Combine("destino", "a.zip")));
    }

    [Fact]
    public void Zip64IsUsedForManyFiles()
    {
        // Más de 65 535 archivos obliga al registro ZIP64 del final.
        using var output = new MemoryStream();
        var writer = new ZipArchiveWriter(output, password: null);
        for (int i = 0; i < 70_000; i++)
        {
            writer.AddFile($"f{i}.txt", new MemoryStream([(byte)i]), DateTime.UtcNow, _ => { }, CancellationToken.None);
        }

        writer.Finish();
        output.Position = 0;
        Assert.Equal(70_000, ZipArchiveReader.Entries(output).Count);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal(70_000, zip.Entries.Count);
        using var stream = zip.GetEntry("f69999.txt")!.Open();
        Assert.Equal(69999 % 256, stream.ReadByte());
    }
}
