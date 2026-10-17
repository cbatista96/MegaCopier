using System.Globalization;
using System.Text.RegularExpressions;

namespace MegaCopier.Engine;

/// <summary>
/// Limpieza de la carpeta de versiones con la regla de "abuelo, padre e hijo" de las copias de seguridad: de cada
/// archivo se guarda la versión más reciente de cada uno de los últimos N días con cambios, de las últimas N semanas y
/// de los últimos N meses; el resto se borra. La versión más reciente siempre se queda.
/// </summary>
public static partial class VersionPruner
{
    /// <summary>"nombre 2026-10-16 153000.ext" o "nombre 2026-10-16 153000 (2).ext" (ver KeepVersion).</summary>
    [GeneratedRegex(@"^(?<name>.+) (?<stamp>\d{4}-\d{2}-\d{2} \d{6})(?: \(\d+\))?$")]
    private static partial Regex VersionName();

    /// <summary>Lee "7,4,12" (días, semanas, meses).</summary>
    public static (int Daily, int Weekly, int Monthly)? Parse(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int d)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int w)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int m)
            ? (d, w, m) : null;
    }

    /// <summary>Qué versiones sobran (sin borrar nada).</summary>
    public static List<string> Excess(string root, int daily, int weekly, int monthly)
    {
        var excess = new List<string>();
        if (daily + weekly + monthly <= 0 || !Directory.Exists(root))
        {
            return excess;
        }

        var groups = new Dictionary<string, List<(string Path, DateTime Time)>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var match = VersionName().Match(Path.GetFileNameWithoutExtension(path));
            if (!match.Success || !DateTime.TryParseExact(match.Groups["stamp"].Value, "yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue; // no es una versión guardada por MegaCopier: no se toca
            }

            string key = Path.Combine(Path.GetDirectoryName(path)!, match.Groups["name"].Value + Path.GetExtension(path));
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add((path, time));
        }

        foreach (var versions in groups.Values)
        {
            var newest = versions.OrderByDescending(v => v.Time).ThenByDescending(v => v.Path, StringComparer.Ordinal).ToList();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { newest[0].Path };
            Keep(newest, v => v.Time.Date, daily, keep);
            Keep(newest, v => (ISOWeek.GetYear(v.Time), ISOWeek.GetWeekOfYear(v.Time)), weekly, keep);
            Keep(newest, v => (v.Time.Year, v.Time.Month), monthly, keep);
            excess.AddRange(newest.Where(v => !keep.Contains(v.Path)).Select(v => v.Path));
        }

        return excess;
    }

    /// <summary>La más reciente de cada uno de los primeros <paramref name="count"/> periodos (de más nuevo a más viejo).</summary>
    private static void Keep<TKey>(List<(string Path, DateTime Time)> newest, Func<(string Path, DateTime Time), TKey> period, int count, HashSet<string> keep)
        where TKey : notnull
    {
        if (count <= 0)
        {
            return;
        }

        var seen = new HashSet<TKey>();
        foreach (var version in newest)
        {
            if (seen.Add(period(version)))
            {
                keep.Add(version.Path);
                if (seen.Count == count)
                {
                    return;
                }
            }
        }
    }
}
