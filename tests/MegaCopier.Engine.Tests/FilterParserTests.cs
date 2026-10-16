namespace MegaCopier.Engine.Tests;

public sealed class FilterParserTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 15, 30, 0, DateTimeKind.Local); // jueves

    [Fact]
    public void SpanishSentence()
    {
        var r = FilterParser.Parse("Fotos de más de 2 MB del último mes sin los .tmp", Now);

        Assert.Contains("*.jpg", r.Filter.Include);
        Assert.Contains("*.heic", r.Filter.Include);
        Assert.Equal(2L * 1024 * 1024, r.Filter.MinSize);
        Assert.Equal(new DateTime(2026, 9, 16).ToUniversalTime(), r.Filter.ModifiedAfterUtc);
        Assert.Equal(["*.tmp"], r.Filter.Exclude);
        Assert.Empty(r.Unknown);
    }

    [Fact]
    public void EnglishSentence()
    {
        var r = FilterParser.Parse("videos larger than 1.5 GB from 2023 except node_modules", Now);

        Assert.Contains("*.mkv", r.Filter.Include);
        Assert.Equal((long)(1.5 * 1024 * 1024 * 1024), r.Filter.MinSize);
        Assert.Equal(new DateTime(2023, 1, 1).ToUniversalTime(), r.Filter.ModifiedAfterUtc);
        Assert.Equal(new DateTime(2024, 1, 1).ToUniversalTime().AddTicks(-1), r.Filter.ModifiedBeforeUtc);
        Assert.Equal(["node_modules"], r.Filter.Exclude);
        Assert.Empty(r.Unknown);
    }

    [Fact]
    public void SizesDatesAndAttributes()
    {
        var r = FilterParser.Parse("documentos y archivos psd de menos de 500 KB de esta semana, sin ocultos ni basura", Now);

        Assert.Contains("*.pdf", r.Filter.Include);
        Assert.Contains("*.psd", r.Filter.Include);
        Assert.Equal(500L * 1024, r.Filter.MaxSize);
        Assert.Equal(new DateTime(2026, 10, 12).ToUniversalTime(), r.Filter.ModifiedAfterUtc); // el lunes
        Assert.True(r.Filter.SkipHidden);
        Assert.True(r.Filter.SkipJunk);
    }

    [Fact]
    public void LastDaysAndBefore()
    {
        var a = FilterParser.Parse("canciones de los últimos 10 días", Now);
        Assert.Contains("*.flac", a.Filter.Include);
        Assert.Equal(new DateTime(2026, 10, 6).ToUniversalTime(), a.Filter.ModifiedAfterUtc);

        var b = FilterParser.Parse("todo menos vídeos, anteriores a 2020", Now);
        Assert.Contains("*.mp4", b.Filter.Exclude);
        Assert.Equal(new DateTime(2020, 1, 1).ToUniversalTime().AddTicks(-1), b.Filter.ModifiedBeforeUtc);
    }

    [Fact]
    public void UnknownWordsAreReported()
    {
        var r = FilterParser.Parse("fotos bonitas de la playa", Now);
        Assert.Contains("*.png", r.Filter.Include);
        Assert.Contains("bonitas", r.Unknown);
        Assert.Contains("playa", r.Unknown);
        Assert.DoesNotContain("*.playa", r.Filter.Include);
    }
}
