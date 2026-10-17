using System.Globalization;
using MegaCopier.Engine;

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
    /// <summary>Una ruta para mostrarla: las de un móvil ("mtp:...") como "Pixel 8\DCIM\Camera".</summary>
    public static string Display(string path) => Engine.Remote.RemotePath.IsRemote(path) ? Engine.Remote.RemotePath.Display(path) : path;

    /// <summary>El nombre del último elemento de la ruta (archivo o carpeta), también en un móvil.</summary>
    public static string ItemName(string path) =>
        Engine.Remote.RemotePath.IsRemote(path) ? Engine.Remote.RemotePath.Name(path) : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    public static string ShortPath(string path, int keep = 2)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(Display(path));
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
        // Las unidades vienen de la tabla de textos ("1 min 05 s", "1 хв 05 с", "1分 05秒").
        if (span.TotalSeconds < 1)
        {
            return Texts.F("U_Seconds", span.TotalSeconds.ToString("0.0", Culture));
        }

        if (span.TotalMinutes < 1)
        {
            return Texts.F("U_Seconds", (int)Math.Ceiling(span.TotalSeconds));
        }

        if (span.TotalHours < 1)
        {
            return Texts.F("U_MinSec", (int)span.TotalMinutes, span.Seconds.ToString("00", Culture));
        }

        return Texts.F("U_HourMin", (int)span.TotalHours, span.Minutes.ToString("00", Culture));
    }

    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("g", Culture);
}
