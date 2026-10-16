using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MegaCopier.Engine;

/// <summary>
/// Filtros escritos como se dicen, en español o inglés: "fotos de más de 2 MB del último mes sin los .tmp",
/// "videos larger than 1 GB from 2023 except node_modules". Todo se interpreta aquí mismo, sin conexión. Lo que no se
/// entiende se devuelve aparte para que la interfaz lo diga.
/// </summary>
public static partial class FilterParser
{
    private static readonly Dictionary<string, string[]> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fotos"] = ["*.jpg", "*.jpeg", "*.png", "*.heic", "*.heif", "*.gif", "*.webp", "*.bmp", "*.tif", "*.tiff", "*.raw", "*.cr2", "*.cr3", "*.nef", "*.arw", "*.dng", "*.orf", "*.rw2"],
        ["videos"] = ["*.mp4", "*.mkv", "*.mov", "*.avi", "*.wmv", "*.m4v", "*.webm", "*.mts", "*.m2ts", "*.3gp"],
        ["musica"] = ["*.mp3", "*.flac", "*.wav", "*.m4a", "*.ogg", "*.aac", "*.wma", "*.opus", "*.aiff"],
        ["documentos"] = ["*.pdf", "*.doc", "*.docx", "*.xls", "*.xlsx", "*.ppt", "*.pptx", "*.odt", "*.ods", "*.odp", "*.txt", "*.rtf", "*.md", "*.csv"],
        ["comprimidos"] = ["*.zip", "*.rar", "*.7z", "*.tar", "*.gz", "*.bz2", "*.xz", "*.iso"],
        ["programas"] = ["*.exe", "*.msi", "*.msix", "*.appx", "*.bat", "*.cmd", "*.ps1"],
        ["temporales"] = ["*.tmp", "*.temp", "*.bak", "~*", "*.part", "*.crdownload"],
    };

    private static readonly Dictionary<string, string> KindWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["foto"] = "fotos", ["fotos"] = "fotos", ["imagen"] = "fotos", ["imagenes"] = "fotos", ["photo"] = "fotos", ["photos"] = "fotos",
        ["picture"] = "fotos", ["pictures"] = "fotos", ["image"] = "fotos", ["images"] = "fotos",
        ["video"] = "videos", ["videos"] = "videos", ["pelicula"] = "videos", ["peliculas"] = "videos", ["movie"] = "videos", ["movies"] = "videos",
        ["musica"] = "musica", ["cancion"] = "musica", ["canciones"] = "musica", ["audio"] = "musica", ["audios"] = "musica", ["music"] = "musica", ["song"] = "musica", ["songs"] = "musica",
        ["documento"] = "documentos", ["documentos"] = "documentos", ["docs"] = "documentos", ["document"] = "documentos", ["documents"] = "documentos",
        ["comprimido"] = "comprimidos", ["comprimidos"] = "comprimidos", ["zips"] = "comprimidos", ["archives"] = "comprimidos", ["archive"] = "comprimidos",
        ["programa"] = "programas", ["programas"] = "programas", ["instaladores"] = "programas", ["programs"] = "programas", ["installers"] = "programas",
        ["temporal"] = "temporales", ["temporales"] = "temporales", ["temp"] = "temporales", ["temporary"] = "temporales",
    };

    private static readonly HashSet<string> NegationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sin", "excepto", "menos", "salvo", "quitando", "no", "ni", "nor", "without", "except", "excluding", "but", "not",
    };

    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "del", "la", "las", "los", "el", "y", "e", "o", "solo", "sólo", "todos", "todas", "archivos", "archivo", "ficheros", "con", "que", "sean", "en", "a", "al",
        "copiar", "copia", "the", "of", "and", "or", "only", "all", "files", "file", "with", "that", "are", "in", "copy", "from", "to", "carpeta", "carpetas", "folder", "folders",
    };

    /// <summary>Resultado: el filtro, lo entendido (para mostrarlo) y lo que no se entendió.</summary>
    public sealed record Result(FileFilter Filter, List<string> Understood, List<string> Unknown);

    public static Result Parse(string text, DateTime now)
    {
        var filter = new FileFilter();
        var understood = new List<string>();
        var unknown = new List<string>();
        string work = " " + Fold(text) + " ";

        // Tamaños: "más de 5 MB", "menos de 100 KB", "> 1 GB", "larger than 2gb".
        foreach (Match m in SizeRegex().Matches(work))
        {
            long bytes = (long)(double.Parse(m.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * Unit(m.Groups["u"].Value));
            bool more = m.Groups["more"].Success;
            if (more)
            {
                filter.MinSize = bytes;
                understood.Add($"> {m.Groups["n"].Value} {m.Groups["u"].Value.ToUpperInvariant()}");
            }
            else
            {
                filter.MaxSize = bytes;
                understood.Add($"< {m.Groups["n"].Value} {m.Groups["u"].Value.ToUpperInvariant()}");
            }

            work = work.Replace(m.Value, " ");
        }

        // Fechas relativas y años.
        ParseDates(ref work, now, filter, understood);

        // Atributos y basura.
        if (Remove(ref work, @"(sin|excepto|without|no|ni|nor) (los |las )?(archivos )?(ocultos|hidden)( files)?"))
        {
            filter.SkipHidden = true;
            understood.Add("sin ocultos");
        }

        if (Remove(ref work, @"(sin|excepto|without|no|ni|nor) (los |las )?(archivos )?(de sistema|del sistema|system)( files)?"))
        {
            filter.SkipSystem = true;
            understood.Add("sin archivos de sistema");
        }

        if (Remove(ref work, @"(sin|excepto|without|no|ni|nor) (la )?(basura|junk)"))
        {
            filter.SkipJunk = true;
            understood.Add("sin basura");
        }

        // Palabras sueltas: tipos, extensiones y nombres; tras "sin/excepto" van a excluir.
        bool exclude = false;
        foreach (string raw in work.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            string word = raw.Trim('.', ':', '"', '\'').Length == 0 ? raw : raw.TrimEnd('.', ':', '"', '\'').TrimStart('"', '\'');
            if (NegationWords.Contains(word))
            {
                exclude = true;
                continue;
            }

            if (FillerWords.Contains(word))
            {
                continue;
            }

            var target = exclude ? filter.Exclude : filter.Include;
            if (KindWords.TryGetValue(word, out string? kind))
            {
                target.AddRange(Kinds[kind].Where(k => !target.Contains(k)));
                understood.Add((exclude ? "sin " : "") + kind);
            }
            else if (ExtensionRegex().IsMatch(word) && (word.Contains('.') || IsKnownExtension(word)))
            {
                string mask = "*." + word.TrimStart('*').TrimStart('.');
                if (!target.Contains(mask))
                {
                    target.Add(mask);
                }

                understood.Add((exclude ? "sin " : "") + mask);
            }
            else if (exclude && word.Length > 1)
            {
                target.Add(word); // un nombre de carpeta o de archivo: "sin node_modules"
                understood.Add("sin " + word);
            }
            else
            {
                unknown.Add(word);
            }
        }

        return new Result(filter, understood, unknown);
    }

    private static void ParseDates(ref string work, DateTime now, FileFilter filter, List<string> understood)
    {
        DateTime today = now.Date;
        void Since(DateTime from, string label)
        {
            filter.ModifiedAfterUtc = from.ToUniversalTime();
            understood.Add(label);
        }

        if (Remove(ref work, @"(de |del )?(hoy|today)"))
        {
            Since(today, "de hoy");
        }
        else if (Remove(ref work, @"(de |desde )?(ayer|yesterday)"))
        {
            Since(today.AddDays(-1), "desde ayer");
        }
        else if (Remove(ref work, @"(de |del )?(esta semana|this week)"))
        {
            Since(today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), "de esta semana");
        }
        else if (Remove(ref work, @"(de |del )?(este mes|this month)"))
        {
            Since(new DateTime(today.Year, today.Month, 1), "de este mes");
        }
        else if (Remove(ref work, @"(de |del )?(este año|este ano|this year)"))
        {
            Since(new DateTime(today.Year, 1, 1), "de este año");
        }

        var last = Regex.Match(work, @"(de |del |de los |de las |en los |en las |in the )?(ultim[oa]s?|last|past) ?(?<n>\d+)? ?(?<u>dias?|semanas?|mes(es)?|años?|anos?|days?|weeks?|months?|years?)");
        if (last.Success)
        {
            int n = last.Groups["n"].Success ? int.Parse(last.Groups["n"].Value, CultureInfo.InvariantCulture) : 1;
            string unit = last.Groups["u"].Value;
            var from = unit.StartsWith("d", StringComparison.Ordinal) ? today.AddDays(-n)
                : unit.StartsWith("s", StringComparison.Ordinal) || unit.StartsWith("w", StringComparison.Ordinal) ? today.AddDays(-7 * n)
                : unit.StartsWith("m", StringComparison.Ordinal) ? today.AddMonths(-n)
                : today.AddYears(-n);
            Since(from, n == 1 ? $"último {unit}" : $"últimos {n} {unit}");
            work = work.Replace(last.Value, " ");
        }

        var before = Regex.Match(work, @"(anteriores a|antes de|before|older than) (?<y>(19|20)\d\d)");
        if (before.Success)
        {
            filter.ModifiedBeforeUtc = new DateTime(int.Parse(before.Groups["y"].Value, CultureInfo.InvariantCulture), 1, 1).ToUniversalTime().AddTicks(-1);
            understood.Add($"antes de {before.Groups["y"].Value}");
            work = work.Replace(before.Value, " ");
        }

        var after = Regex.Match(work, @"(posteriores a|despues de|desde|after|since) (?<y>(19|20)\d\d)");
        if (after.Success)
        {
            filter.ModifiedAfterUtc = new DateTime(int.Parse(after.Groups["y"].Value, CultureInfo.InvariantCulture), 1, 1).ToUniversalTime();
            understood.Add($"desde {after.Groups["y"].Value}");
            work = work.Replace(after.Value, " ");
        }

        var year = Regex.Match(work, @"(de |del |en |from |in )(el año |el ano |the year )?(?<y>(19|20)\d\d)\b");
        if (year.Success)
        {
            int y = int.Parse(year.Groups["y"].Value, CultureInfo.InvariantCulture);
            filter.ModifiedAfterUtc = new DateTime(y, 1, 1).ToUniversalTime();
            filter.ModifiedBeforeUtc = new DateTime(y + 1, 1, 1).ToUniversalTime().AddTicks(-1);
            understood.Add($"de {y}");
            work = work.Replace(year.Value, " ");
        }
    }

    private static readonly HashSet<string> MoreExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "psd", "ai", "svg", "eps", "indd", "dwg", "dxf", "skp", "blend", "fbx", "obj", "stl", "sql", "db", "sqlite", "json", "xml", "html", "htm",
        "css", "js", "ts", "py", "cs", "java", "cpp", "h", "log", "ini", "cfg", "dll", "sys", "lnk", "eml", "msg", "pst", "ost", "vhd", "vhdx",
        "vmdk", "epub", "mobi", "srt", "nfo", "torrent", "ttf", "otf", "woff", "apk", "dmg", "img", "pdn", "kra", "xcf", "afphoto", "lrcat",
    };

    private static bool IsKnownExtension(string word) =>
        MoreExtensions.Contains(word) || Kinds.Values.Any(masks => masks.Contains("*." + word, StringComparer.OrdinalIgnoreCase));

    private static bool Remove(ref string work, string pattern)
    {
        var match = Regex.Match(work, pattern);
        if (!match.Success)
        {
            return false;
        }

        work = work.Remove(match.Index, match.Length).Insert(match.Index, " ");
        return true;
    }

    private static double Unit(string unit) => unit.ToUpperInvariant() switch
    {
        "B" or "BYTES" => 1,
        "KB" or "K" => 1024,
        "MB" or "M" or "MEGAS" => 1024 * 1024,
        "GB" or "G" or "GIGAS" => 1024d * 1024 * 1024,
        _ => 1024d * 1024 * 1024 * 1024,
    };

    /// <summary>Minúsculas y sin tildes ("vídeos" → "videos"), con los símbolos separados.</summary>
    private static string Fold(string text)
    {
        var builder = new StringBuilder();
        foreach (char c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c is '>' or '<' ? $" {c} " : c.ToString());
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"(?:(?<more>mas de|mayores de|mayor de|mas grandes que|de mas de|larger than|bigger than|over|more than|>)|(?:menos de|menores de|menor de|mas pequenos que|smaller than|under|less than|<))\s*(?<n>\d+(?:[.,]\d+)?)\s*(?<u>bytes|kb|mb|gb|tb|megas|gigas|k|m|g|b)\b")]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"^(\*?\.)?[a-z0-9]{1,6}$")]
    private static partial Regex ExtensionRegex();
}
