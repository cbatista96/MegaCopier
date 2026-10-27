using System.Security.Cryptography;
using System.Text;

namespace MegaCopier.Engine.Tests;

/// <summary>Comprobar un archivo contra una suma copiada de una web, y el CRC32 que va en el nombre.</summary>
public sealed class HashMatchTests
{
    [Theory]
    [InlineData("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824")]
    [InlineData("SHA256: 2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824")]
    [InlineData("5d41402abc4b2a76b9719d911017c592  hola.iso", "5d41402abc4b2a76b9719d911017c592")]
    [InlineData("0x3610A686", "3610a686")]
    [InlineData("versión 2024 del archivo", null)]
    [InlineData("abc", null)]
    public void ExtractsTheChecksumFromPastedText(string text, string? expected) =>
        Assert.Equal(expected, HashMatch.Extract(text));

    [Fact]
    public void TellsWhichAlgorithmMatches()
    {
        using var temp = new TempDir();
        string file = temp.Combine("descarga.iso");
        File.WriteAllText(file, "hola mundo");
        byte[] bytes = Encoding.UTF8.GetBytes("hola mundo");

        var sha256 = HashMatch.Check(file, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.True(sha256.Matches);
        Assert.Equal(FileHashAlgorithm.Sha256, sha256.Algorithm);

        var md5 = HashMatch.Check(file, Convert.ToHexStringLower(MD5.HashData(bytes)));
        Assert.True(md5.Matches);
        Assert.Equal(FileHashAlgorithm.Md5, md5.Algorithm);

        var sha512 = HashMatch.Check(file, Convert.ToHexStringLower(SHA512.HashData(bytes)));
        Assert.Equal(FileHashAlgorithm.Sha512, sha512.Algorithm);
        Assert.True(sha512.Matches);

        var wrong = HashMatch.Check(file, new string('a', 64));
        Assert.False(wrong.Matches);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), wrong.Actual);
    }

    [Theory]
    [InlineData("Serie - 01 [A1B2C3D4].mkv", "a1b2c3d4")]
    [InlineData("video (0f1e2d3c).mp4", "0f1e2d3c")]
    [InlineData("video [1080p].mkv", null)]
    [InlineData("informe.pdf", null)]
    public void ReadsTheCrcInTheName(string name, string? crc) =>
        Assert.Equal(crc, HashMatch.CrcInName(name));

    [Fact]
    public void TestingAFolderChecksTheCrcInTheNames()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.Combine("descargas"));
        byte[] bytes = Encoding.UTF8.GetBytes("capítulo");
        var crc = new System.IO.Hashing.Crc32();
        crc.Append(bytes);
        string good = crc.GetCurrentHashAsUInt32().ToString("X8");
        File.WriteAllBytes(temp.Combine("descargas", $"cap 01 [{good}].mkv"), bytes);
        File.WriteAllBytes(temp.Combine("descargas", "cap 02 [DEADBEEF].mkv"), bytes);

        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Test });
        var result = engine.Run(new CopyJobRequest([temp.Combine("descargas")], temp.Combine("x")));

        Assert.Single(result.Failures);
        Assert.Contains("cap 02", result.Failures[0].Source);
        Assert.Equal(1, engine.Files.Snapshot().Count(i => i.State == FileState.Verified));
    }
}
