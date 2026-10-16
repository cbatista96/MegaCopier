using System.Globalization;

namespace MegaCopier.App.ViewModels;

/// <summary>Tamaños, velocidades, duraciones y fechas con el formato regional del usuario.</summary>
internal static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

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
        int unit = 0;
        while (bytes >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Format(Culture, "{0:0} {1}", bytes, Units[unit])
            : string.Format(Culture, bytes >= 100 ? "{0:0} {1}" : "{0:0.0#} {1}", bytes, Units[unit]);
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
