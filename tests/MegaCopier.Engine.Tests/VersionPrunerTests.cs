namespace MegaCopier.Engine.Tests;

public sealed class VersionPrunerTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Version(string name, DateTime when, string folder = "")
    {
        string directory = Path.Combine(_temp.Path, folder);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)} {when:yyyy-MM-dd HHmmss}{Path.GetExtension(name)}");
        File.WriteAllText(path, when.ToString("O"));
        return path;
    }

    [Fact]
    public void KeepsTheNewestOfEachDayWeekAndMonth()
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0);
        var all = new List<string>();
        for (int day = 0; day < 120; day++)
        {
            all.Add(Version("informe.docx", now.AddDays(-day)));
            all.Add(Version("informe.docx", now.AddDays(-day).AddHours(-3))); // dos al día: sobra la más vieja
        }

        string other = Version("otro.txt", now.AddDays(-400)); // la única versión de otro archivo: se queda
        File.WriteAllText(Path.Combine(_temp.Path, "mío.txt"), "no es una versión");

        var excess = VersionPruner.Excess(_temp.Path, 7, 4, 12).ToHashSet();
        var kept = all.Where(p => !excess.Contains(p)).ToList();

        Assert.DoesNotContain(other, excess);
        Assert.DoesNotContain(Path.Combine(_temp.Path, "mío.txt"), excess);
        Assert.Contains(all[0], kept); // la más reciente
        Assert.All(Enumerable.Range(0, 7), d => Assert.Contains(all[d * 2], kept)); // 7 días
        Assert.DoesNotContain(all[1], kept); // la segunda del mismo día
        // 7 días + hasta 4 semanas + hasta 12 meses, solapándose: entre 7 y 7+4+12.
        Assert.InRange(kept.Count, 8, 23);
        Assert.Contains(kept, p => File.ReadAllText(p).StartsWith("2026-07", StringComparison.Ordinal)); // la de julio (4 meses atrás)
    }

    [Fact]
    public void ZeroKeepsEverythingAndOneKeepsOnlyTheLatest()
    {
        var now = DateTime.Now;
        Version("a.txt", now.AddDays(-1));
        string latest = Version("a.txt", now);
        Assert.Empty(VersionPruner.Excess(_temp.Path, 0, 0, 0));
        var excess = VersionPruner.Excess(_temp.Path, 1, 0, 0);
        Assert.Single(excess);
        Assert.DoesNotContain(latest, excess);
    }

    [Fact]
    public void ACopyWithVersionsPrunesAtTheEnd()
    {
        string source = Path.Combine(_temp.Path, "origen");
        string target = Path.Combine(_temp.Path, "destino");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(target, "origen"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "versión nueva");
        File.WriteAllText(Path.Combine(target, "origen", "a.txt"), "viejo");
        string versions = Path.Combine(target, ".versiones");
        for (int i = 1; i <= 5; i++)
        {
            string folder = Path.Combine(versions, "origen");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"a {DateTime.Now.AddDays(-i):yyyy-MM-dd HHmmss}.txt"), $"v{i}");
        }

        var engine = new CopyEngine(new CopyOptions { VersionsFolder = ".versiones", VersionsDaily = 1, Conflict = ConflictPolicy.Overwrite });
        var result = engine.Run(new CopyJobRequest([source], target));

        Assert.Empty(result.Failures);
        Assert.Equal("versión nueva", File.ReadAllText(Path.Combine(target, "origen", "a.txt")));
        var left = Directory.GetFiles(Path.Combine(versions, "origen"));
        Assert.Single(left); // solo la que se acaba de apartar ("viejo")
        Assert.Equal("viejo", File.ReadAllText(left[0]));
    }

    [Theory]
    [InlineData("7,4,12", 7, 4, 12)]
    [InlineData("0, 0, 1", 0, 0, 1)]
    public void Parses(string text, int d, int w, int m) => Assert.Equal((d, w, m), VersionPruner.Parse(text));

    [Fact]
    public void RejectsBadText() => Assert.Null(VersionPruner.Parse("7,4"));
}
