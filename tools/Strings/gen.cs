// Genera Strings\<idioma>\Resources.resw de la app a partir de tools/Strings/strings.tsv (clave, es-ES, en-US).
// Uso: dotnet run tools/Strings/gen.cs -- <tsv> <carpeta Strings de la app>
using System.Security;
using System.Text;

string tsv = args[0];
string output = args[1];
var lines = File.ReadAllLines(tsv, Encoding.UTF8).Where(l => l.Length > 0).ToList();
string[] languages = lines[0].Split('\t')[1..];
var rows = lines.Skip(1).Select(l => l.Split('\t')).ToList();

var seen = new HashSet<string>();
foreach (var row in rows)
{
    if (row.Length != languages.Length + 1 || row.Skip(1).Any(string.IsNullOrEmpty))
    {
        throw new InvalidDataException($"Fila incompleta: {row[0]}");
    }

    if (!seen.Add(row[0]))
    {
        throw new InvalidDataException($"Clave repetida: {row[0]}");
    }
}

for (int lang = 0; lang < languages.Length; lang++)
{
    var sb = new StringBuilder();
    sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    sb.AppendLine("<!-- Generado por tools/Strings/gen.cs a partir de strings.tsv: no editar a mano. -->");
    sb.AppendLine("<root>");
    sb.AppendLine("  <resheader name=\"resmimetype\"><value>text/microsoft-resx</value></resheader>");
    sb.AppendLine("  <resheader name=\"version\"><value>2.0</value></resheader>");
    sb.AppendLine("  <resheader name=\"reader\"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>");
    sb.AppendLine("  <resheader name=\"writer\"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>");
    foreach (var row in rows)
    {
        sb.AppendLine($"  <data name=\"{SecurityElement.Escape(row[0])}\" xml:space=\"preserve\"><value>{SecurityElement.Escape(row[lang + 1])}</value></data>");
    }

    sb.AppendLine("</root>");
    string folder = Path.Combine(output, languages[lang]);
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "Resources.resw"), sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"{languages[lang]}: {rows.Count} textos");
}
