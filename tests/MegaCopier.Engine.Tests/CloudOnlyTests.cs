namespace MegaCopier.Engine.Tests;

/// <summary>Archivos que solo están en la nube (OneDrive «solo en línea»): se avisa de que se descargarán, o se omiten.</summary>
public sealed class CloudOnlyTests
{
    [Theory]
    [InlineData(FileAttributes.Normal, false)]
    [InlineData(FileAttributes.Offline, true)]
    [InlineData((FileAttributes)0x40000, true)] // RECALL_ON_OPEN
    [InlineData((FileAttributes)0x400000, true)] // RECALL_ON_DATA_ACCESS (OneDrive)
    [InlineData(FileAttributes.ReparsePoint, false)]
    public void RecognizesCloudPlaceholders(FileAttributes attributes, bool cloudOnly) =>
        Assert.Equal(cloudOnly, FileFilter.IsCloudOnly(attributes));

    [Fact]
    public void WarnsOrSkipsFilesThatAreOnlyInTheCloud()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.Combine("OneDrive"));
        File.WriteAllText(temp.Combine("OneDrive", "local.txt"), "aquí");
        string online = temp.Combine("OneDrive", "en-la-nube.txt");
        File.WriteAllText(online, "nube");
        File.SetAttributes(online, FileAttributes.Offline); // como un marcador de OneDrive
        try
        {
            var plan = CopyPlanner.Build([temp.Combine("OneDrive")], temp.Combine("destino"));
            Assert.Equal(2, plan.Files.Count);
            Assert.Contains(plan.Warnings, w => w.Contains('1'));

            var skip = CopyPlanner.Build([temp.Combine("OneDrive")], temp.Combine("destino"), new FileFilter { SkipCloudOnly = true });
            Assert.Equal(["local.txt"], skip.Files.Select(f => Path.GetFileName(f.Source)));
            Assert.Empty(skip.Warnings);
        }
        finally
        {
            File.SetAttributes(online, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData("fotos sin los que solo están en la nube")]
    [InlineData("photos without cloud-only files")]
    public void NaturalLanguageCanSkipThem(string text) =>
        Assert.True(FilterParser.Parse(text, DateTime.Now).Filter.SkipCloudOnly);
}
