using System.Text.Json;
using Microsoft.Win32;

namespace MegaCopier.App.Services;

/// <summary>
/// Idioma de la interfaz. Se aplica al arrancar (antes de cargar el XAML) y se publica en el registro para que la
/// extensión del Explorador use el mismo idioma.
/// </summary>
internal static class LanguageSetting
{
    private const string Key = @"Software\MegaCopier";

    /// <summary>Lee el idioma de settings.json sin cargar el resto y lo aplica a los recursos de la app.</summary>
    public static void ApplyAtStartup()
    {
        string language = "";
        try
        {
            if (File.Exists(AppSettings.FilePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(AppSettings.FilePath));
                if (document.RootElement.TryGetProperty("Language", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    language = value.GetString() ?? "";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (Exception)
        {
            // Si no se puede fijar, se usa el idioma de Windows.
        }

        // Los textos del motor (registro, errores) siguen el mismo idioma que la ventana, en todos los hilos. Sin idioma
        // elegido, el de Windows; si no hay traducción para él, inglés (lo mismo que hacen los recursos de la ventana).
        var culture = System.Globalization.CultureInfo.GetCultureInfo(language.Length > 0 ? language : Engine.Texts.LanguageFor(System.Globalization.CultureInfo.CurrentUICulture));
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;

        if (!App.TestMode)
        {
            Save(language);
        }
    }

    /// <summary>Publica el idioma ("" = el de Windows) para la extensión del Explorador.</summary>
    public static void Save(string language)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue("Language", language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
