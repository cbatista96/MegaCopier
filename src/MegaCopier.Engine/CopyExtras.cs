namespace MegaCopier.Engine;

/// <summary>
/// Opciones menos habituales de una copia, juntas para que viajen de una vez entre Ajustes, la copia guardada y el
/// motor: lo que hacen Robocopy, Total Commander o Directory Opus y no hace falta casi nunca.
/// </summary>
public sealed class CopyExtras
{
    /// <summary>Quitar la marca «Archivo» de cada origen copiado bien (copias incrementales clásicas, Robocopy /M).</summary>
    public bool ResetArchive { get; set; }

    /// <summary>
    /// Un archivo que solo difiere en una hora exacta se da por igual: tras el cambio de horario, los discos FAT y exFAT
    /// guardan la hora local y parecería que cambió todo (Robocopy /DST).
    /// </summary>
    public bool CompensateDst { get; set; }

    /// <summary>Lo que Windows tiene comprimido (atributo «Comprimido» de NTFS) sigue comprimido en el destino.</summary>
    public bool KeepNtfsCompression { get; set; }

    /// <summary>Todos los archivos a la carpeta de destino, sin sus subcarpetas (los repetidos se numeran).</summary>
    public bool Flatten { get; set; }

    /// <summary>
    /// Archivos sueltos de varias carpetas: se recrean sus carpetas desde la que tienen en común (por ejemplo,
    /// D:\Fotos\2024\a.jpg y D:\Fotos\2025\b.jpg llegan como 2024\a.jpg y 2025\b.jpg).
    /// </summary>
    public bool KeepRelativePaths { get; set; }

    /// <summary>
    /// Renombrar al copiar, como en Total Commander: «*.bak» cambia la extensión, «copia de *.*» antepone un texto,
    /// «*_2024.*» añade al final. Antes del punto, * es el nombre; después, la extensión. Vacío = sin cambios.
    /// </summary>
    public string RenameMask { get; set; } = "";

    /// <summary>Orden que se ejecuta antes de empezar (parar un servicio, cerrar un programa); si falla, la copia no empieza.</summary>
    public string BeforeCommand { get; set; } = "";

    /// <summary>Fechas de las carpetas: null = como las de los archivos.</summary>
    public bool? FolderTimestamps { get; set; }

    /// <summary>Atributos de las carpetas (oculta, solo lectura…): null = como los de los archivos.</summary>
    public bool? FolderAttributes { get; set; }

    public bool IsDefault =>
        !ResetArchive && !CompensateDst && !KeepNtfsCompression && !Flatten && !KeepRelativePaths && RenameMask.Length == 0
        && BeforeCommand.Length == 0 && FolderTimestamps is null && FolderAttributes is null;

    public CopyExtras Clone() => (CopyExtras)MemberwiseClone();

    /// <summary>El nombre con la máscara aplicada («foto.jpg» con «*.bak» → «foto.bak»); sin máscara, el mismo.</summary>
    public static string ApplyMask(string name, string mask)
    {
        mask = mask.Trim();
        if (mask.Length == 0 || mask is "*" or "*.*")
        {
            return name;
        }

        string extension = Path.GetExtension(name);
        string stem = name[..^extension.Length];
        string ext = extension.TrimStart('.');
        int dot = mask.LastIndexOf('.');
        if (dot < 0)
        {
            return mask.Replace("*", name, StringComparison.Ordinal);
        }

        string newStem = mask[..dot].Replace("*", stem, StringComparison.Ordinal);
        string newExt = mask[(dot + 1)..].Replace("*", ext, StringComparison.Ordinal);
        return newExt.Length == 0 ? newStem : $"{newStem}.{newExt}";
    }
}
