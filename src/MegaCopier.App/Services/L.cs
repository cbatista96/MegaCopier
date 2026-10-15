using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace MegaCopier.App.Services;

/// <summary>
/// Textos traducidos para el código (los del XAML usan x:Uid). Están en Strings\{idioma}\Resources.resw y el idioma
/// lo decide Windows o el ajuste de la app (ApplicationLanguages.PrimaryLanguageOverride al arrancar).
/// </summary>
internal static class L
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key)
    {
        string text = Loader.GetString(key);
        return string.IsNullOrEmpty(text) ? key : text;
    }

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
