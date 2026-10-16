namespace MegaCopier.Engine.Tests;

public sealed class FilterTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void IncludeExcludeSizeAndFolders()
    {
        string source = _temp.Combine("src");
        Directory.CreateDirectory(Path.Combine(source, "node_modules", "lib"));
        Directory.CreateDirectory(Path.Combine(source, "fotos"));
        File.WriteAllBytes(Path.Combine(source, "fotos", "a.jpg"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(source, "fotos", "pequeña.jpg"), new byte[10]);
        File.WriteAllBytes(Path.Combine(source, "fotos", "b.png"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(source, "node_modules", "lib", "c.jpg"), new byte[2000]);
        string hidden = Path.Combine(source, "fotos", "oculta.jpg");
        File.WriteAllBytes(hidden, new byte[2000]);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var options = new CopyOptions
        {
            ProgressInterval = TimeSpan.Zero,
            Filter = new FileFilter { Include = ["*.jpg"], Exclude = ["node_modules"], MinSize = 100, SkipHidden = true },
        };
        var destination = _temp.Combine("dst");
        var result = new CopyEngine(options).Run(new CopyJobRequest([source], destination));

        Assert.True(result.Success);
        var copied = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(destination, p)).ToList();
        Assert.Equal([Path.Combine("src", "fotos", "a.jpg")], copied);
    }

    [Fact]
    public void EmptyFilterCopiesEverything()
    {
        string source = _temp.Combine("src");
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, "x.bin"), new byte[5]);
        var destination = _temp.Combine("dst");
        var result = new CopyEngine(new CopyOptions { ProgressInterval = TimeSpan.Zero }).Run(new CopyJobRequest([source], destination));
        Assert.Equal(1, result.FilesCopied);
    }
}
