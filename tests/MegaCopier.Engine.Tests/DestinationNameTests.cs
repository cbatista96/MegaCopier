using System.Text;

namespace MegaCopier.Engine.Tests;

/// <summary>Nombres que el destino no admite: se arreglan al planificar; los válidos no se tocan.</summary>
public sealed class DestinationNameTests
{
    private static readonly DestinationNames.Limits Local = DestinationNames.Limits.Default;
    private static readonly DestinationNames.Limits Network = new(255, LimitBytes: true);

    [Theory]
    [InlineData("informe.pdf", "informe.pdf")]
    [InlineData("Fotos de mamá", "Fotos de mamá")]
    [InlineData(".gitignore", ".gitignore")]
    [InlineData("nombre ", "nombre")]
    [InlineData("nombre. . ", "nombre")]
    [InlineData("¿qué? *dudas*.txt", "¿qué_ _dudas_.txt")]
    [InlineData("a:b|c\"d<e>f.txt", "a_b_c_d_e_f.txt")]
    [InlineData("CON", "CON_")]
    [InlineData("CON .txt", "CON_.txt")]
    [InlineData("nul.txt", "nul_.txt")]
    [InlineData("COM1.tar.gz", "COM1_.tar.gz")]
    [InlineData("CONSOLA.txt", "CONSOLA.txt")]
    [InlineData("   ", "_")]
    public void FixesOnlyWhatTheDestinationRejects(string name, string expected) =>
        Assert.Equal(expected, DestinationNames.Fix(name, Local));

    [Fact]
    public void ShortensLongNamesKeepingTheExtension()
    {
        string name = new string('a', 300) + ".docx";
        string fixedName = DestinationNames.Fix(name, Local);
        Assert.Equal(255, fixedName.Length);
        Assert.EndsWith(".docx", fixedName, StringComparison.Ordinal);
    }

    [Fact]
    public void OnNetworkSharesCountsBytesNotCharacters()
    {
        // 200 letras con tilde: 200 caracteres (valen en NTFS) pero 400 bytes (no en un NAS con Linux).
        string name = new string('á', 200) + ".jpg";
        Assert.Equal(name, DestinationNames.Fix(name, Local));
        string fixedName = DestinationNames.Fix(name, Network);
        Assert.True(Encoding.UTF8.GetByteCount(fixedName) <= 255);
        Assert.EndsWith(".jpg", fixedName, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotSplitEmojiWhenShortening()
    {
        string name = string.Concat(Enumerable.Repeat("😀", 200)) + ".txt";
        string fixedName = DestinationNames.Fix(name, Local);
        Assert.True(fixedName.Length <= 255);
        Assert.DoesNotContain('\uFFFD', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(fixedName)));
    }

    [Fact]
    public void CopiesANameWithTrailingSpacesUnderAValidName()
    {
        using var source = new TempDir();
        using var destination = new TempDir();
        string folder = source.Combine("Carpeta");
        Directory.CreateDirectory(folder);
        // Con \\?\ Windows deja crear «nota .txt »: el Explorador no lo abre y una memoria exFAT lo rechaza.
        File.WriteAllText(@"\\?\" + Path.Combine(folder, "nota.txt "), "uno");
        File.WriteAllText(Path.Combine(folder, "nota.txt"), "dos");

        var engine = new CopyEngine(new CopyOptions { Verify = true, RetryCount = 0 });
        var result = engine.Run(new CopyJobRequest([folder], destination.Path));

        Assert.True(result.Success, string.Join("\n", result.Failures.Select(f => f.Source + ": " + f.Message)));
        string copied = destination.Combine("Carpeta");
        var names = Directory.GetFiles(copied).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["nota (2).txt", "nota.txt"], names);
        Assert.Equal("dos", File.ReadAllText(Path.Combine(copied, "nota.txt")));
        Assert.Equal("uno", File.ReadAllText(Path.Combine(copied, "nota (2).txt")));
    }
}
