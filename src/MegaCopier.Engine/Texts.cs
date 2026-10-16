using System.Globalization;

namespace MegaCopier.Engine;

/// <summary>
/// Textos del motor que ve el usuario (registro, errores, avisos) en el idioma de la interfaz
/// (<see cref="CultureInfo.CurrentUICulture"/>; la app lo fija al arrancar). Las tablas se generan en Texts.g.cs desde
/// tools/Strings/engine.tsv y tools/Strings/i18n/*.engine.tsv. Un idioma sin traducción usa el inglés.
/// </summary>
public static partial class Texts
{
    private const string Fallback = "en-US";

    /// <summary>Idiomas disponibles: código ("fr-FR") y nombre en ese idioma ("Français").</summary>
    public static IReadOnlyList<(string Code, string Name)> Languages => LanguageNames;

    public static string Get(string key)
    {
        var table = TableFor(CultureInfo.CurrentUICulture);
        return table.TryGetValue(key, out string? text) || Tables[Fallback].TryGetValue(key, out text) ? text : key;
    }

    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>El idioma con traducción que corresponde a una cultura ("es-MX" → "es-ES", "zh-Hans-CN" → "zh-CN").</summary>
    public static string LanguageFor(CultureInfo culture)
    {
        for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            if (Tables.ContainsKey(current.Name))
            {
                return current.Name;
            }

            // Mismo idioma, otro país o escritura: la primera traducción de ese idioma.
            string language = current.TwoLetterISOLanguageName;
            if (LanguageNames.FirstOrDefault(l => l.Code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)) is { Code: { } code })
            {
                return code;
            }
        }

        return Fallback;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, string>> ByCulture = new();

    private static Dictionary<string, string> TableFor(CultureInfo culture) => ByCulture.GetOrAdd(culture.Name, _ => Tables[LanguageFor(culture)]);
}
