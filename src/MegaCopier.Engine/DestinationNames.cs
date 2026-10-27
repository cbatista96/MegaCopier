using System.Text;

namespace MegaCopier.Engine;

/// <summary>
/// Nombres que el destino no admite, arreglados al planificar la copia (como TeraCopy 4): sin espacios ni puntos al
/// final, sin caracteres prohibidos en Windows, sin nombres reservados (CON, NUL, COM1…) y recortados si son más largos
/// de lo que acepta el disco, conservando la extensión. En las carpetas de red el límite es de 255 bytes (los NAS con
/// Linux cuentan bytes, y una letra con tilde ocupa dos). Un nombre válido no se toca nunca.
/// </summary>
public static class DestinationNames
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>Lo que acepta el destino: caracteres por nombre y, en red, también bytes UTF-8.</summary>
    public readonly record struct Limits(int MaxChars, bool LimitBytes)
    {
        public static Limits Default { get; } = new(255, false);

        public static Limits For(string destination)
        {
            int max = 255;
            try
            {
                if (Native.Win32.MaxNameLength(destination) is int reported and > 0)
                {
                    max = Math.Min(255, reported);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }

            return new Limits(max, IsNetwork(destination));
        }

        private static bool IsNetwork(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                return Path.GetPathRoot(Path.GetFullPath(path)) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>El nombre arreglado, o el mismo si ya vale.</summary>
    public static string Fix(string name, Limits limits)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            builder.Append(c < 32 || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? '_' : c);
        }

        string fixedName = TrimEnd(builder.ToString());
        if (fixedName.Length == 0)
        {
            fixedName = "_";
        }

        int dot = fixedName.IndexOf('.');
        string stem = dot < 0 ? fixedName : fixedName[..dot];
        if (Reserved.Contains(stem.TrimEnd(' ')))
        {
            fixedName = stem.TrimEnd(' ') + "_" + (dot < 0 ? "" : fixedName[dot..]);
        }

        return Shorten(fixedName, limits);
    }

    /// <summary>Otro nombre libre para cuando el arreglado coincide con uno que ya hay: «nombre (2).ext».</summary>
    public static string Numbered(string name, int number, Limits limits)
    {
        string extension = Path.GetExtension(name);
        string stem = name[..^extension.Length];
        string suffix = $" ({number})";
        string candidate = stem + suffix + extension;
        while (TooLong(candidate, limits) && stem.Length > 1)
        {
            stem = CutLast(stem);
            candidate = TrimEnd(stem) + suffix + extension;
        }

        return candidate;
    }

    private static string Shorten(string name, Limits limits)
    {
        if (!TooLong(name, limits))
        {
            return name;
        }

        // Se conserva la extensión (si es razonable) y se recorta el nombre.
        string extension = Path.GetExtension(name);
        if (extension.Length is 0 or > 16)
        {
            extension = "";
        }

        string stem = name[..^extension.Length];
        while (stem.Length > 1 && TooLong(TrimEnd(stem) + extension, limits))
        {
            stem = CutLast(stem);
        }

        string result = TrimEnd(stem) + extension;
        return result.Length == 0 ? "_" + extension : result;
    }

    private static bool TooLong(string name, Limits limits) =>
        name.Length > limits.MaxChars || (limits.LimitBytes && Encoding.UTF8.GetByteCount(name) > 255);

    /// <summary>Quita el último carácter sin partir un par sustituto (emoji y otros fuera del plano básico).</summary>
    private static string CutLast(string text) =>
        text.Length >= 2 && char.IsLowSurrogate(text[^1]) && char.IsHighSurrogate(text[^2]) ? text[..^2] : text[..^1];

    private static string TrimEnd(string name) => name.TrimEnd(' ', '.');
}
