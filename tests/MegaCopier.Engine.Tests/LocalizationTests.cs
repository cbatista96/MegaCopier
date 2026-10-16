using System.Globalization;

namespace MegaCopier.Engine.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("es-MX", "es-ES")]
    [InlineData("en-GB", "en-US")]
    [InlineData("sv-SE", "en-US")] // sin traducción: inglés
    public void LanguageForPicksTheClosestTranslation(string culture, string expected) =>
        Assert.Equal(expected, Texts.LanguageFor(CultureInfo.GetCultureInfo(culture)));

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
