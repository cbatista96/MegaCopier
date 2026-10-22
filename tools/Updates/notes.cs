// Extrae las notas bilingües completas de una versión. No contacta con ningún servidor.
using System.Text;
using System.Text.RegularExpressions;

if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] != "--require-summary" || !Regex.IsMatch(args[0], @"^\d+\.\d+\.\d+$")) throw new ArgumentException("Uso: notes.cs version carpeta-salida [--require-summary]");
string version = args[0], directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
var localized = new Dictionary<string, string>();
foreach (string language in new[] { "es", "en" })
{
    string source = File.ReadAllText(language == "es" ? "CHANGELOG.md" : "CHANGELOG.en.md");
    var match = Regex.Match(source, @"(?ms)^## \[" + Regex.Escape(version) + @"\][^\n]*\n(.*?)(?=^## \[|^\[[^\]]+\]:|\z)", RegexOptions.None, TimeSpan.FromSeconds(1));
    if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value)) throw new InvalidDataException("Faltan las notas " + language + " de " + version);
    string notes = match.Groups[1].Value.Trim();
    // Las publicaciones nuevas empiezan por un resumen editorial. Las antiguas siguen siendo exportables.
    var summary = Regex.Match(notes, @"(?ims)^### (?:Resumen|Highlights)\s*\r?\n(.*?)(?=^### |\z)", RegexOptions.None, TimeSpan.FromSeconds(1));
    int count = Regex.Matches(summary.Groups[1].Value, @"(?m)^- ", RegexOptions.None, TimeSpan.FromSeconds(1)).Count;
    if (args.Length == 3 && (!summary.Success || count < 1))
        throw new InvalidDataException("Falta un resumen editorial con novedades principales en " + language);
    if (summary.Success)
    {
        string heading = language == "es" ? "### Resumen" : "### Highlights";
        notes = heading + "\n\n" + summary.Groups[1].Value.Trim() + "\n\n" + notes.Remove(summary.Index, summary.Length).Trim();
    }
    // En una página de release, las rutas relativas no apuntan al árbol del repositorio.
    // Fijar la etiqueta mantiene las capturas de esta versión aunque main cambie después.
    notes = Regex.Replace(notes, "(?<prefix>(?:src|href)\\s*=\\s*\")(?<url>[^\"]+)(?<suffix>\")", m =>
        m.Groups["prefix"].Value + PublicUrl(m.Groups["url"].Value, m.Groups["prefix"].Value.StartsWith("src", StringComparison.OrdinalIgnoreCase)) + m.Groups["suffix"].Value,
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    notes = Regex.Replace(notes, @"(?<prefix>!?\[[^\]\r\n]*\]\()(?<url>[^\s)]+)(?<suffix>\))", m =>
        m.Groups["prefix"].Value + PublicUrl(m.Groups["url"].Value, m.Groups["prefix"].Value.StartsWith('!')) + m.Groups["suffix"].Value,
        RegexOptions.None, TimeSpan.FromSeconds(1));
    File.WriteAllText(Path.Combine(directory, "release-notes." + language + ".md"), notes.Trim() + "\n", new UTF8Encoding(false));
    localized[language] = notes.Trim();
}

// GitHub tiene un único cuerpo por publicación: ambos idiomas visibles, sin depender del idioma de la cuenta.
string body = "[English](#english) · [Español](#español)\n\n## English\n\n" + localized["en"]
    + "\n\n**Install:** download `MegaCopier-Setup-" + version + ".exe` (64-bit Windows 11). SHA-256 checksums are in `SHA256SUMS.txt`. "
    + "For Windows code-signing status, see the [code-signing policy](https://github.com/cbatista96/MegaCopier/blob/main/docs/Code-signing-policy.md).\n\n---\n\n## Español\n\n" + localized["es"]
    + "\n\n**Instalar:** descarga `MegaCopier-Setup-" + version + ".exe` (Windows 11, 64 bits). Las sumas SHA-256 están en `SHA256SUMS.txt`. "
    + "Consulta el estado de la firma de Windows en la [política de firma](https://github.com/cbatista96/MegaCopier/blob/main/docs/Code-signing-policy.md).\n";
File.WriteAllText(Path.Combine(directory, "release-body.md"), body, new UTF8Encoding(false));

string PublicUrl(string value, bool image)
{
    if (value.StartsWith('#') || value.StartsWith("//", StringComparison.Ordinal) || Uri.TryCreate(value, UriKind.Absolute, out _)) return value;
    string root = image ? "https://raw.githubusercontent.com/cbatista96/MegaCopier/v" + version + "/"
        : "https://github.com/cbatista96/MegaCopier/blob/v" + version + "/";
    return new Uri(new Uri(root), value).AbsoluteUri;
}
