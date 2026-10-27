using Microsoft.Win32;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>Pruebas: hacer como si el destino tuviera este sistema de archivos.</summary>
    internal Func<string, string?>? FileSystemOverride { get; set; }

    /// <summary>Lo máximo que admite un archivo en FAT32 (4 GB menos un byte).</summary>
    private const long Fat32MaxFile = uint.MaxValue;

    /// <summary>
    /// Comprobación previa, antes de copiar nada: lo que no puede salir bien se marca ya como error (con el motivo) en
    /// lugar de fallar a mitad, y lo que puede dar problemas se avisa en el registro.
    /// </summary>
    private void Preflight(string destination, CopyJobResult result)
    {
        var pending = Files.Snapshot().Where(i => i.State == FileState.Pending).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        string? fileSystem = FileSystemOverride?.Invoke(destination) ?? FileSystemOf(destination);
        bool fat32 = fileSystem is "FAT32" or "FAT";
        bool fat = fat32 || fileSystem == "exFAT";

        // FAT32 no admite archivos de 4 GB o más: se sabe desde el principio.
        if (fat32)
        {
            foreach (var item in pending.Where(i => i.Length > Fat32MaxFile))
            {
                string message = Texts.Get("E_Fat32");
                result.Failures.Add(new FileFailure(item.Source, message, FileErrorKind.TooLargeForFileSystem));
                Files.Update(item, FileState.Failed, message, FileErrorKind.TooLargeForFileSystem);
                Log(LogLevel.Error, message, item.Source);
            }
        }

        if (fat && (_options.CopyStreams || _options.CopySecurity != Native.SecurityParts.None))
        {
            Log(LogLevel.Warning, Texts.F("E_FatLimits", fileSystem));
        }

        // Dos archivos que en el destino serían el mismo (solo cambian mayúsculas, de carpetas sensibles a mayúsculas o WSL).
        int collisions = pending.GroupBy(i => i.Destination, StringComparer.OrdinalIgnoreCase).Count(g => g.Count() > 1);
        if (collisions > 0)
        {
            Log(LogLevel.Warning, Texts.F("E_CaseCollisions", collisions));
        }

        // Los nombres que Windows o el destino no admiten (CON, acabados en espacio, demasiado largos) ya llegan
        // arreglados del plan (DestinationNames), con su aviso.

        // Rutas largas: MegaCopier las copia, pero sin la opción de rutas largas de Windows otros programas no las abren.
        int longPaths = pending.Count(i => i.Destination.Length >= 260);
        if (longPaths > 0 && !LongPathsEnabled())
        {
            Log(LogLevel.Warning, Texts.F("E_LongPaths", longPaths));
        }
    }

    /// <summary>Sistema de archivos del destino ("NTFS", "exFAT", "FAT32", "ReFS"...), o null si no se sabe.</summary>
    internal static string? FileSystemOf(string path)
    {
        try
        {
            string? root = Native.Win32.VolumeRoot(Path.GetFullPath(path));
            return root is null ? null : new DriveInfo(root).DriveFormat;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool LongPathsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            return key?.GetValue("LongPathsEnabled") is int value && value == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
