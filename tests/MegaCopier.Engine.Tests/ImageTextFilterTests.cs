namespace MegaCopier.Engine.Tests;

[Collection("ImageText")] // el lector es estático
public sealed class ImageTextFilterTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly Func<string, string?>? _previous = FileFilter.ImageTextReader;

    public void Dispose()
    {
        FileFilter.ImageTextReader = _previous;
        _temp.Dispose();
    }

    [Theory]
    [InlineData("fotos con el texto \"Total a pagar\"", "total a pagar")]
    [InlineData("capturas que digan factura del último mes", "factura")]
    [InlineData("images containing 'INVOICE 2026'", "invoice 2026")]
    public void TheParserUnderstandsIt(string text, string expected)
    {
        var result = FilterParser.Parse(text, new DateTime(2026, 10, 17));
        Assert.Equal(expected, result.Filter.ImageText);
        Assert.Contains(result.Understood, u => u.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnlyImagesWhoseTextMatchesAreCopied()
    {
        string source = _temp.Combine("capturas");
        Directory.CreateDirectory(source);
        var texts = new Dictionary<string, string>
        {
            ["tique.jpg"] = "SUPERMERCADO\nTOTAL A PAGAR 23,40 €",
            ["paisaje.png"] = "",
            ["factura.PNG"] = "Factura nº 77 · Total  a   PAGAR: 100",
            ["nota.txt"] = "total a pagar", // no es una imagen
        };
        foreach (var (name, _) in texts)
        {
            File.WriteAllText(Path.Combine(source, name), "x");
        }

        int reads = 0;
        FileFilter.ImageTextReader = path =>
        {
            Interlocked.Increment(ref reads);
            return texts[Path.GetFileName(path)];
        };
        var filter = FilterParser.Parse("fotos con el texto 'total a pagar'", DateTime.Now).Filter;
        var result = new CopyEngine(new CopyOptions { Filter = filter }).Run(new CopyJobRequest([source], _temp.Combine("destino")));

        Assert.Equal(2, result.FilesCopied);
        Assert.True(File.Exists(_temp.Combine("destino", "capturas", "tique.jpg")));
        Assert.True(File.Exists(_temp.Combine("destino", "capturas", "factura.PNG")));
        Assert.False(File.Exists(_temp.Combine("destino", "capturas", "nota.txt")));
        Assert.Equal(3, reads); // solo las imágenes
    }

    [Fact]
    public void WithoutAReaderNothingPasses()
    {
        FileFilter.ImageTextReader = null;
        var file = new FileInfo(_temp.Combine("a.jpg"));
        File.WriteAllText(file.FullName, "x");
        Assert.False(new FileFilter { ImageText = "hola" }.AcceptsFile("a.jpg", file));
        Assert.True(new FileFilter().AcceptsFile("a.jpg", file));
    }
}
