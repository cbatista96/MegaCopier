namespace MegaCopier.Engine.Jobs;

/// <summary>Un disco reconocido por su número de serie y su nombre, no por su letra (que Windows puede cambiar).</summary>
public sealed record VolumeRef(string Root, string Serial, string Label);

/// <summary>
/// Reconocer los discos de una tarea aunque Windows les dé otra letra (como las «letras variables» de FreeFileSync): se
/// guarda el número de serie de cada uno y, al ejecutarla, las rutas se pasan a la letra que tenga ahora ese disco.
/// </summary>
public static class Volumes
{
    /// <summary>El disco de una ruta local ("F:\..."), o null si es de red, no existe o no se puede leer.</summary>
    public static VolumeRef? Identify(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (root is not { Length: 3 } || root[1] != ':')
            {
                return null; // solo unidades con letra; las de red se reconocen por su nombre
            }

            return Native.Win32.VolumeSerial(root) is { } serial
                ? new VolumeRef(root, serial.ToString("X8"), Native.Win32.VolumeLabel(root) ?? "")
                : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Número de serie (en hexadecimal) del disco de esa raíz ("F:\"), o null si no hay disco.</summary>
    public static string? SerialOf(string root) => Native.Win32.VolumeSerial(root)?.ToString("X8");

    /// <summary>Anota los discos de los orígenes y del destino de una tarea guardada.</summary>
    public static void Remember(CopyJobRecord record)
    {
        record.Volumes = [.. record.Sources.Append(record.Destination).Concat(record.ExtraTargets.Keys).Concat(record.ExtraTargets.Values)
            .Select(Identify).OfType<VolumeRef>()
            .DistinctBy(v => v.Root, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>La letra que tiene ahora el disco con este número de serie, o null si no está conectado.</summary>
    public static string? CurrentRoot(string serial, Func<IEnumerable<string>>? roots = null)
    {
        foreach (string root in (roots ?? MountedRoots)())
        {
            if (Native.Win32.VolumeSerial(root) is { } current && current.ToString("X8") == serial)
            {
                return root;
            }
        }

        return null;
    }

    /// <summary>
    /// Corrige las rutas de la tarea si algún disco cambió de letra. Devuelve cuántos discos se movieron; los que no están
    /// conectados se quedan como estaban (la copia avisará de que falta).
    /// </summary>
    public static int Remap(CopyJobRecord record, Func<string, string?>? serialOf = null, Func<string, string?>? find = null)
    {
        serialOf ??= root => Native.Win32.VolumeSerial(root)?.ToString("X8");
        find ??= serial => CurrentRoot(serial);
        int moved = 0;
        foreach (var volume in record.Volumes.ToList())
        {
            if (serialOf(volume.Root) == volume.Serial || find(volume.Serial) is not { } now
                || string.Equals(now, volume.Root, StringComparison.OrdinalIgnoreCase))
            {
                continue; // en su sitio, o no conectado
            }

            string Move(string path) => path.StartsWith(volume.Root, StringComparison.OrdinalIgnoreCase) ? now + path[volume.Root.Length..] : path;
            record.Sources = [.. record.Sources.Select(Move)];
            record.Destination = Move(record.Destination);
            record.Excluded = [.. record.Excluded.Select(Move)];
            record.FileOrder = [.. record.FileOrder.Select(Move)];
            record.ExtraTargets = new(record.ExtraTargets.ToDictionary(p => Move(p.Key), p => Move(p.Value), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            record.Volumes[record.Volumes.IndexOf(volume)] = volume with { Root = now };
            moved++;
        }

        return moved;
    }

    private static IEnumerable<string> MountedRoots()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is DriveType.Removable or DriveType.Fixed && drive.IsReady)
            {
                yield return drive.RootDirectory.FullName;
            }
        }
    }
}
