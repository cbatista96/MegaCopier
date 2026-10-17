namespace MegaCopier.Engine.Tests;

public sealed class WipeTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(WipeMethod.Zeros, "0")]
    [InlineData(WipeMethod.Random, "?")]
    [InlineData(WipeMethod.Dod3, "0F?")]
    [InlineData(WipeMethod.Dod7, "0F??0F?")]
    public void EachPassOverwritesTheWholeFileWithItsPattern(WipeMethod method, string expected)
    {
        // Algo más de 1 MiB para que cada pasada necesite más de un bloque.
        byte[] original = new byte[1_300_000];
        Array.Fill(original, (byte)0x5A);
        string file = _temp.Combine("secreto.bin");
        File.WriteAllBytes(file, original);

        var seen = new List<char>();
        long lastTotal = 0;
        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Delete, SecureDelete = true, WipeMethod = method, ProgressInterval = TimeSpan.Zero })
        {
            AfterWipePass = stream =>
            {
                byte[] content = new byte[stream.Length];
                stream.Position = 0;
                stream.ReadExactly(content);
                seen.Add(content.All(b => b == 0) ? '0' : content.All(b => b == 0xFF) ? 'F' : content.Count(b => b == 0x5A) < content.Length / 100 ? '?' : 'x');
            },
        };
        engine.ProgressChanged += p => lastTotal = p.TotalBytes;

        var result = engine.Run(new CopyJobRequest([file], _temp.Path));

        Assert.Empty(result.Failures);
        Assert.Equal(expected, new string([.. seen]));
        Assert.False(File.Exists(file));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path));
        Assert.Equal(original.Length * (long)expected.Length, lastTotal); // la barra cuenta todas las pasadas
    }
}
