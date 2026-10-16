// Genera los textos traducidos de MegaCopier:
//   - Strings\<idioma>\Resources.resw de la app, desde strings.tsv (clave, es-ES, en-US) e i18n\<idioma>.tsv (clave, texto).
//   - Texts.g.cs del motor, desde engine.tsv (clave, es-ES, en-US) e i18n\<idioma>.engine.tsv (clave, texto).
// Los idiomas y su nombre están en i18n\languages.tsv (código, nombre en ese idioma).
// Uso: dotnet run tools/Strings/gen.cs -- <carpeta tools/Strings> <carpeta Strings de la app> <Texts.g.cs del motor>
// Falla si a un idioma le falta una clave, le sobra o no tiene los mismos huecos {0} que el inglés (la app fallaría).
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

string root = args[0];
string appOutput = args[1];
string engineOutput = args[2];

var languages = Read(Path.Combine(root, "i18n", "languages.tsv"), 2).Select(r => (Code: r[0], Name: r[1])).ToList();
var app = Load("strings.tsv", ".tsv");
var engine = Load("engine.tsv", ".engine.tsv");

foreach (var (code, _) in languages)
{
    WriteResw(code, app);
}

WriteEngine(engine);
Console.WriteLine($"{languages.Count} idiomas · {app.Keys.Count} textos de la app · {engine.Keys.Count} del motor");

// Tabla base (es-ES y en-US) más un archivo por cada otro idioma; todo comprobado contra el inglés.
Texts Load(string baseFile, string suffix)
{
    var rows = Read(Path.Combine(root, baseFile), 3);
    var keys = rows.Select(r => r[0]).ToList();
    var byLanguage = new Dictionary<string, Dictionary<string, string>>
    {
        ["es-ES"] = rows.ToDictionary(r => r[0], r => r[1]),
        ["en-US"] = rows.ToDictionary(r => r[0], r => r[2]),
    };
    if (keys.Distinct().Count() != keys.Count)
    {
        throw new InvalidDataException($"{baseFile}: claves repetidas: {string.Join(", ", keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key))}");
    }

    foreach (var (code, _) in languages.Where(l => l.Code is not ("es-ES" or "en-US")))
    {
        string file = Path.Combine(root, "i18n", code + suffix);
        var table = Read(file, 2).ToDictionary(r => r[0], r => r[1]);
        var missing = keys.Where(k => !table.ContainsKey(k)).ToList();
        var extra = table.Keys.Where(k => !keys.Contains(k)).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            throw new InvalidDataException($"{file}: faltan [{string.Join(", ", missing.Take(20))}] sobran [{string.Join(", ", extra.Take(20))}]");
        }

        byLanguage[code] = table;
    }

    foreach (var (code, table) in byLanguage)
    {
        foreach (string key in keys)
        {
            if (!Holes(table[key]).SetEquals(Holes(byLanguage["en-US"][key])))
            {
                throw new InvalidDataException($"{code} {key}: los huecos {{n}} no coinciden con el inglés: «{table[key]}»");
            }
        }
    }

    return new Texts(keys, byLanguage);
}

static HashSet<string> Holes(string text) => [.. Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value)];

static List<string[]> Read(string path, int columns)
{
    var rows = new List<string[]>();
    foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Skip(1).Where(l => l.Length > 0))
    {
        var row = line.Split('\t');
        if (row.Length != columns || row.Any(string.IsNullOrEmpty))
        {
            throw new InvalidDataException($"{Path.GetFileName(path)}: fila mal formada: {line}");
        }

        rows.Add(row);
    }

    return rows;
}

void WriteResw(string code, Texts texts)
{
    var sb = new StringBuilder();
    sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    sb.AppendLine("<!-- Generado por tools/Strings/gen.cs: no editar a mano. -->");
    sb.AppendLine("<root>");
    sb.AppendLine("  <resheader name=\"resmimetype\"><value>text/microsoft-resx</value></resheader>");
    sb.AppendLine("  <resheader name=\"version\"><value>2.0</value></resheader>");
    sb.AppendLine("  <resheader name=\"reader\"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>");
    sb.AppendLine("  <resheader name=\"writer\"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>");
    foreach (string key in texts.Keys)
    {
        sb.AppendLine($"  <data name=\"{SecurityElement.Escape(key)}\" xml:space=\"preserve\"><value>{SecurityElement.Escape(texts.ByLanguage[code][key])}</value></data>");
    }

    sb.AppendLine("</root>");
    string folder = Path.Combine(appOutput, code);
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "Resources.resw"), sb.ToString(), new UTF8Encoding(false));
}

void WriteEngine(Texts texts)
{
    // "\n" en la tabla es un salto de línea (la ayuda de mcopy).
    static string Literal(string text) =>
        "\"" + text.Replace("\\n", "\n").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated> Generado por tools/Strings/gen.cs: no editar a mano. </auto-generated>");
    sb.AppendLine("namespace MegaCopier.Engine;");
    sb.AppendLine();
    sb.AppendLine("public static partial class Texts");
    sb.AppendLine("{");
    sb.AppendLine("    private static readonly (string Code, string Name)[] LanguageNames =");
    sb.AppendLine("    [");
    foreach (var (code, name) in languages)
    {
        sb.AppendLine($"        ({Literal(code)}, {Literal(name)}),");
    }

    sb.AppendLine("    ];");
    sb.AppendLine();
    sb.AppendLine("    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new(StringComparer.OrdinalIgnoreCase)");
    sb.AppendLine("    {");
    foreach (var (code, _) in languages)
    {
        sb.AppendLine($"        [{Literal(code)}] = new()");
        sb.AppendLine("        {");
        foreach (string key in texts.Keys)
        {
            sb.AppendLine($"            [{Literal(key)}] = {Literal(texts.ByLanguage[code][key])},");
        }

        sb.AppendLine("        },");
    }

    sb.AppendLine("    };");
    sb.AppendLine("}");
    File.WriteAllText(engineOutput, sb.ToString(), new UTF8Encoding(false));
}

record Texts(List<string> Keys, Dictionary<string, Dictionary<string, string>> ByLanguage);
