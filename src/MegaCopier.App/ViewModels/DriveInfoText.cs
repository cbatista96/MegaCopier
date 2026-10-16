using System.Collections.Concurrent;

namespace MegaCopier.App.ViewModels;

/// <summary>Descripción de la unidad de una ruta: "D: Datos" con su icono, sistema de archivos y espacio libre.</summary>
public sealed record DriveDescription(string Letter, string Label, string Glyph, string Format, string Kind, long TotalBytes, long FreeBytes)
{
    public string Text => string.IsNullOrEmpty(Label) ? Letter : $"{Letter} {Label}";

    public double UsedFraction => TotalBytes > 0 ? 1 - ((double)FreeBytes / TotalBytes) : 0;
}

internal static class DriveInfoText
{
    private static readonly ConcurrentDictionary<string, (DriveDescription Description, DateTime At)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Datos de la unidad (se refrescan como mucho cada 5 s: el espacio libre cambia durante la copia).</summary>
    public static DriveDescription Describe(string path)
    {
        string root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
        }
        catch (ArgumentException)
        {
            root = path;
        }

        if (Cache.TryGetValue(root, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(5))
        {
            return cached.Description;
        }

        DriveDescription description;
        try
        {
            var drive = new DriveInfo(root);
            string letter = root.StartsWith(@"\\", StringComparison.Ordinal) ? root.TrimEnd('\\') : root.TrimEnd('\\');
            string glyph = drive.DriveType switch
            {
                DriveType.Removable => "",  // USB
                DriveType.Network => "",    // red
                DriveType.CDRom => "",      // disco óptico
                _ => "",                    // disco duro
            };
            string kind = drive.DriveType switch
            {
                DriveType.Removable => "USB",
                DriveType.Network => "Red",
                DriveType.CDRom => "CD/DVD",
                DriveType.Ram => "RAM",
                _ => "Disco",
            };
            description = drive.IsReady
                ? new DriveDescription(letter, drive.VolumeLabel, glyph, drive.DriveFormat, kind, drive.TotalSize, drive.AvailableFreeSpace)
                : new DriveDescription(letter, "", glyph, "", kind, 0, 0);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            description = new DriveDescription(root.TrimEnd('\\'), "", "", "", "", 0, 0);
        }

        Cache[root] = (description, DateTime.UtcNow);
        return description;
    }
}
