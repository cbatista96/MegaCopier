// Genera la información de privacidad del asistente a partir de los documentos revisables.
using System.Text;
using System.Text.RegularExpressions;

string root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
foreach (string language in new[] { "es", "en" })
{
    string document = language == "es" ? "Privacidad.md" : "Privacy.md";
    string text = File.ReadAllText(Path.Combine(root, "docs", document));
    text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");
    text = Regex.Replace(text, @"(?m)^#+\s*", "").Replace("**", "").Replace("`", "");
    var rtf = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 ");
    foreach (char value in text.Replace("\r", ""))
        if (value == '\n') rtf.Append(@"\par ");
        else if (value is '\\' or '{' or '}') rtf.Append('\\').Append(value);
        else if (value > 127) rtf.Append(@"\u").Append(unchecked((short)value)).Append('?');
        else rtf.Append(value);
    rtf.Append('}');
    File.WriteAllText(Path.Combine(root, "installer", "Assets", $"Privacy.{language}.rtf"), rtf.ToString(), Encoding.ASCII);
}
Console.WriteLine("Información de privacidad del instalador generada en español e inglés.");
