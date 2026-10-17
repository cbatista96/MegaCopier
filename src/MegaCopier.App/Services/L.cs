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
        try
        {
            string text = Loader.GetString(key);
            return string.IsNullOrEmpty(text) ? key : text;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // Un texto que no existe no puede tumbar la app (WinUI lanza 0x80073B17 en vez de devolver vacío).
            return key;
        }
    }

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
