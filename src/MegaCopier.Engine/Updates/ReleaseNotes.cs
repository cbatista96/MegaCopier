using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MegaCopier.Engine.Updates;

public enum NoteKind { Paragraph, Heading, ListItem, Image, Code, Rule }
public sealed record NoteBlock(NoteKind Kind, string Text, int Level = 0, string? Url = null, int ImageWidth = 0, int ImageGroup = 0);
public sealed record NoteInline(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Url = null);
public sealed record ChangelogVersion(string Version, string Date, string Notes);

/// <summary>Markdown limitado a documentos: produce datos, nunca ejecuta HTML ni genera XAML remoto.</summary>
public static class ReleaseNotes
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);
    private static MatchCollection Matches(string text, string pattern) => Regex.Matches(text, pattern, RegexOptions.CultureInvariant, Limit);

    /// <summary>Publicaciones antiguas sin assets traducidos: escoger solo el bloque del idioma, sin mezclar ambos.</summary>
    public static string LocalizedBody(string markdown, bool spanish)
    {
        var headings = Matches(markdown, @"(?m)^## (English|Español)[ \t]*\r?$");
        string language = spanish ? "Español" : "English";
        for (int i = 0; i < headings.Count; i++)
        {
            if (headings[i].Groups[1].Value != language) continue;
            int start = headings[i].Index + headings[i].Length;
            int end = i + 1 < headings.Count ? headings[i + 1].Index : markdown.Length;
            return markdown[start..end].Trim();
        }
        return headings.Count == 0 && spanish ? markdown : "";
    }

    /// <summary>Resumen editorial de la publicación; las versiones antiguas usan hasta cinco puntos sin imágenes.</summary>
    public static string Summary(string markdown)
    {
        var explicitSummary = Regex.Match(markdown, @"(?ims)^### (?:Resumen|Highlights|Summary)\s*\r?\n(.*?)(?=^#{1,3} |\z)", RegexOptions.CultureInvariant, Limit);
        var blocks = Parse(explicitSummary.Success ? explicitSummary.Groups[1].Value : markdown);
        var points = blocks.Where(b => b.Kind == NoteKind.ListItem && b.Level == 0);
        var items = (explicitSummary.Success ? points : points.Take(5)).ToList();
        if (items.Count == 0) items = blocks.Where(b => b.Kind == NoteKind.Paragraph).Take(1).ToList();
        return string.Join("\n\n", items.Select(b => "- " + (explicitSummary.Success ? string.Concat(Inline(b.Text).Select(i => i.Text)) : Shorten(b.Text))));
    }

    private static string Shorten(string text)
    {
        // Texto plano para cortar sin dejar enlaces o delimitadores Markdown incompletos.
        string plain = string.Concat(Inline(text).Select(i => i.Text));
        if (plain.Length <= 280) return plain;
        int end = plain.LastIndexOf(' ', 277, 80);
        return plain[..(end > 0 ? end : 277)].TrimEnd() + "…";
    }

    public static IReadOnlyList<ChangelogVersion> History(string markdown, ReleaseVersion installed)
    {
        var headers = Matches(markdown, @"(?m)^## \[([^\]\r\n]+)\]([^\r\n]*)");
        var result = new List<ChangelogVersion>();
        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var version = ReleaseVersion.Parse(header.Groups[1].Value);
            if (version is null || version.CompareTo(installed) > 0) continue;
            int end = i + 1 < headers.Count ? headers[i + 1].Index : markdown.Length;
            result.Add(new(version.ToString(), header.Groups[2].Value.Trim(' ', '-'),
                markdown[(header.Index + header.Length)..end].Trim()));
        }
        return result.DistinctBy(v => v.Version).OrderByDescending(v => ReleaseVersion.Parse(v.Version)).ToList();
    }

    public static IReadOnlyList<NoteBlock> Parse(string markdown)
    {
        if (markdown.Length > 2 * 1024 * 1024) throw new UpdateException(UpdateError.TooLarge);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Matches(markdown, @"(?m)^\[([^\]\r\n]+)\]:\s*(\S+)"))
            references[match.Groups[1].Value] = match.Groups[2].Value;
        var result = new List<NoteBlock>();
        var paragraph = new StringBuilder();
        var code = new StringBuilder();
        bool fenced = false;
        void Flush()
        {
            if (paragraph.Length > 0) { result.Add(new(NoteKind.Paragraph, paragraph.ToString().Trim())); paragraph.Clear(); }
        }
        foreach (string raw in markdown.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                if (fenced) { result.Add(new(NoteKind.Code, code.ToString().TrimEnd())); code.Clear(); }
                fenced = !fenced;
                continue;
            }
            if (fenced) { code.AppendLine(raw); continue; }
            // El historial usa tablas HTML solo para colocar capturas. Conservamos imágenes y texto, sin mostrar sus etiquetas.
            line = Regex.Replace(line, @"(?i)</?(?:table|tbody|thead|tr|td|th|a)\b[^>]*>|<br\s*/?>", " ", RegexOptions.CultureInvariant, Limit).Trim();
            if (line.Length == 0) { Flush(); continue; }
            if (Regex.IsMatch(line, @"^\[[^\]]+\]:\s*\S+", RegexOptions.CultureInvariant, Limit)) continue;
            foreach (var reference in references)
                line = line.Replace("][" + reference.Key + "]", "](" + reference.Value + ")", StringComparison.OrdinalIgnoreCase);
            var images = Matches(line, @"!\[([^\]]*)\]\(([^\s)]+)(?:\s+[^)]*)?\)|<img\b[^>]*>");
            if (images.Count > 0)
            {
                Flush();
                int offset = 0;
                int group = images.Count > 1 ? result.Count + 1 : 0;
                foreach (Match image in images)
                {
                    if (image.Index > offset && line[offset..image.Index].Trim().Length > 0)
                        result.Add(new(NoteKind.Paragraph, line[offset..image.Index].Trim()));
                    string url = image.Groups[2].Value, alt = image.Groups[1].Value;
                    int width = 0;
                    if (url.Length == 0)
                    {
                        foreach (Match attribute in Matches(image.Value, "(?i)(src|alt|width)\\s*=\\s*[\"']([^\"']*)[\"']"))
                        {
                            if (attribute.Groups[1].Value.Equals("src", StringComparison.OrdinalIgnoreCase)) url = attribute.Groups[2].Value;
                            else if (attribute.Groups[1].Value.Equals("alt", StringComparison.OrdinalIgnoreCase)) alt = attribute.Groups[2].Value;
                            else if (int.TryParse(attribute.Groups[2].Value, out int parsed)) width = Math.Clamp(parsed, 0, 1600);
                        }
                    }
                    if (url.Length > 0) result.Add(new(NoteKind.Image, WebUtility.HtmlDecode(alt), Url: WebUtility.HtmlDecode(url), ImageWidth: width, ImageGroup: group));
                    offset = image.Index + image.Length;
                }
                if (offset < line.Length && line[offset..].Trim().Length > 0) result.Add(new(NoteKind.Paragraph, line[offset..].Trim()));
                continue;
            }
            int heading = line.TakeWhile(c => c == '#').Count();
            if (heading is > 0 and <= 6 && line.Length > heading && line[heading] == ' ')
            {
                Flush(); result.Add(new(NoteKind.Heading, line[(heading + 1)..].TrimEnd(' ', '#'), heading)); continue;
            }
            if (line is "---" or "***" or "___") { Flush(); result.Add(new(NoteKind.Rule, "")); continue; }
            var list = Regex.Match(line, @"^(?:[-*+] |\d+\. )(.+)$", RegexOptions.CultureInvariant, Limit);
            if (list.Success) { Flush(); result.Add(new(NoteKind.ListItem, list.Groups[1].Value, Math.Min(8, (raw.Length - raw.TrimStart().Length) / 2))); continue; }
            if (paragraph.Length == 0 && raw.Length > raw.TrimStart().Length && result.LastOrDefault() is { Kind: NoteKind.ListItem } previous)
            {
                result[^1] = previous with { Text = previous.Text + " " + line }; continue;
            }
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line);
        }
        Flush();
        if (code.Length > 0) result.Add(new(NoteKind.Code, code.ToString()));
        return result;
    }

    public static IReadOnlyList<NoteInline> Inline(string text, int depth = 0)
    {
        if (depth > 8) return [new(text)];
        var result = new List<NoteInline>();
        var plain = new StringBuilder();
        void Flush() { if (plain.Length > 0) { result.Add(new(WebUtility.HtmlDecode(plain.ToString()))); plain.Clear(); } }
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length) { plain.Append(text[++i]); continue; }
            if (text[i] == '[')
            {
                int middle = text.IndexOf("](", i, StringComparison.Ordinal), end = middle < 0 ? -1 : text.IndexOf(')', middle + 2);
                if (middle > i && end > middle)
                {
                    Flush(); result.Add(new(text[(i + 1)..middle], Url: text[(middle + 2)..end])); i = end; continue;
                }
            }
            string token = text[i] == '`' ? "`" : text[i] is '*' or '_' ? new string(text[i], i + 1 < text.Length && text[i + 1] == text[i] ? 2 : 1) : "";
            int close = token.Length == 0 ? -1 : text.IndexOf(token, i + token.Length, StringComparison.Ordinal);
            if (close > i + token.Length)
            {
                Flush();
                string inner = text[(i + token.Length)..close];
                if (token == "`") result.Add(new(inner, Code: true));
                else foreach (var item in Inline(inner, depth + 1)) result.Add(item with { Bold = item.Bold || token.Length == 2, Italic = item.Italic || token.Length == 1 });
                i = close + token.Length - 1;
            }
            else plain.Append(text[i]);
        }
        Flush();
        return result;
    }
}
