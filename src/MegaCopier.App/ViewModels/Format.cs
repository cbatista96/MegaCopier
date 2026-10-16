using System.Globalization;

namespace MegaCopier.App.ViewModels;

/// <summary>Tamaños, velocidades, duraciones y fechas con el formato regional del usuario.</summary>
internal static class Format
{
    private static readonly string[] Classic = ["B", "KB", "MB", "GB", "TB"];
    private static readonly string[] Iec = ["B", "KiB", "MiB", "GiB", "TiB"];
    private static readonly string[] DecimalUnits = ["B", "kB", "MB", "GB", "TB"];

    /// <summary>Unidades elegidas en Ajustes.</summary>
    public static Services.SizeUnits SizeUnits { get; set; }

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>
    /// Ruta acortada por el medio para que se vea la carpeta final: "C:\Users\…\scratchpad\dest1". La completa va en el tooltip.
    /// </summary>
    public static string ShortPath(string path, int keep = 2)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(path);
        string root = Path.GetPathRoot(trimmed) ?? "";
        string[] parts = trimmed[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= keep + 1
            ? trimmed
            : root + parts[0] + Path.DirectorySeparatorChar + "…" + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, parts[^keep..]);
    }

    public static string Bytes(double bytes)
    {
        var units = SizeUnits switch { Services.SizeUnits.Iec => Iec, Services.SizeUnits.Decimal => DecimalUnits, _ => Classic };
        double step = SizeUnits == Services.SizeUnits.Decimal ? 1000 : 1024;
        int unit = 0;
        while (bytes >= step && unit < units.Length - 1)
        {
            bytes /= step;
            unit++;
        }

        return unit == 0
            ? string.Format(Culture, "{0:0} {1}", bytes, units[unit])
            : string.Format(Culture, bytes >= 100 ? "{0:0} {1}" : "{0:0.0#} {1}", bytes, units[unit]);
    }

    public static string Speed(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 1)
        {
            return string.Format(Culture, "{0:0.0} s", span.TotalSeconds);
        }

        if (span.TotalMinutes < 1)
        {
            return $"{(int)Math.Ceiling(span.TotalSeconds)} s";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes} min {span.Seconds:00} s";
        }

        return $"{(int)span.TotalHours} h {span.Minutes:00} min";
    }

    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("g", Culture);
}
