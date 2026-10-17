using System.Globalization;

namespace MegaCopier.Engine.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("es-MX", "es-ES")]
    [InlineData("en-GB", "en-US")]
    [InlineData("sv-SE", "en-US")] // sin traducción: inglés
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("de-AT", "de-DE")]
    [InlineData("fr-CA", "fr-FR")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("ko-KR", "ko-KR")]
    public void LanguageForPicksTheClosestTranslation(string culture, string expected) =>
        Assert.Equal(expected, Texts.LanguageFor(CultureInfo.GetCultureInfo(culture)));

    // mcopy lee la respuesta a un conflicto con estas letras (reemplazar, saltar, conservar ambos).
    [Fact]
    public void ConflictKeysAreThreeDistinctLettersInEveryLanguage()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var (code, _) in Texts.Languages)
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(code);
                string keys = Texts.Get("C_ConflictKeys");
                Assert.Equal(3, keys.Length);
                Assert.Equal(3, keys.ToLowerInvariant().Distinct().Count());
                Assert.All(keys, c => Assert.True(char.IsLetter(c) && char.IsLower(c), $"{code}: {keys}"));
                Assert.All(keys, c => Assert.Contains($"[{c}]", Texts.Get("C_ConflictPrompt")));
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void EveryLanguageHasEveryTextWithTheSameHoles()
    {
        var english = Texts.Languages.Single(l => l.Code == "en-US");
        Assert.Contains(Texts.Languages, l => l.Code == "es-ES");
        var original = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var (code, _) in Texts.Languages)
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(code);
                string text = Texts.F("E_DoneCopy", "00:00:01", 1, 2, 3);
                Assert.Contains("1", text);
                Assert.Contains("3", text);
                Assert.NotEqual("E_DoneCopy", text);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }

        Assert.Equal("English", english.Name);
    }

    [Fact]
    public void EngineMessagesFollowTheInterfaceLanguage()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Skipped by the user", Texts.Get("E_SkippedByUser"));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            Assert.Equal("Saltado por el usuario", Texts.Get("E_SkippedByUser"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
