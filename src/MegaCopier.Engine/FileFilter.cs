using System.IO.Enumeration;

namespace MegaCopier.Engine;

/// <summary>
/// Qué archivos entran en la copia (como los filtros de Ultracopier y FastCopy): máscaras de inclusión y exclusión
/// ("*.jpg", "node_modules", "temp\*"), tamaño, fecha de modificación y atributos ocultos o de sistema.
/// Las máscaras se comparan con el nombre y con la ruta relativa al origen. Una carpeta excluida se salta entera.
/// </summary>
public sealed class FileFilter
{
    public List<string> Include { get; set; } = [];

    public List<string> Exclude { get; set; } = [];

    public long? MinSize { get; set; }

    public long? MaxSize { get; set; }

    public DateTime? ModifiedAfterUtc { get; set; }

    public DateTime? ModifiedBeforeUtc { get; set; }

    public bool SkipHidden { get; set; }

    public bool SkipSystem { get; set; }

    /// <summary>Omitir archivos "basura" que crean Windows, macOS y Office (<see cref="JunkMasks"/>).</summary>
    public bool SkipJunk { get; set; }

    /// <summary>Miniaturas, configuración de carpetas, metadatos de macOS y archivos de bloqueo de Office.</summary>
    public static readonly string[] JunkMasks = ["Thumbs.db", "ehthumbs.db", "desktop.ini", ".DS_Store", "._*", "~$*", "$RECYCLE.BIN", "System Volume Information"];

    public bool IsEmpty => Include.Count == 0 && Exclude.Count == 0 && MinSize is null && MaxSize is null
        && ModifiedAfterUtc is null && ModifiedBeforeUtc is null && !SkipHidden && !SkipSystem && !SkipJunk;

    public bool AcceptsFile(string relativePath, FileInfo file) =>
        AcceptsFile(relativePath, file.Length, file.LastWriteTimeUtc) && !HasSkippedAttributes(file.Attributes);

    /// <summary>Lo mismo para un archivo que no está en el disco (un móvil): sin atributos y quizá sin fecha.</summary>
    public bool AcceptsFile(string relativePath, long length, DateTime? lastWriteUtc)
    {
        string name = Path.GetFileName(relativePath);
        if (Exclude.Any(mask => Matches(mask, name, relativePath)) || IsJunk(name))
        {
            return false;
        }

        if (Include.Count > 0 && !Include.Any(mask => Matches(mask, name, relativePath)))
        {
            return false;
        }

        if ((MinSize is { } min && length < min) || (MaxSize is { } max && length > max))
        {
            return false;
        }

        return lastWriteUtc is not { } modified
            || !((ModifiedAfterUtc is { } after && modified < after) || (ModifiedBeforeUtc is { } before && modified > before));
    }

    public bool AcceptsDirectory(string relativePath, FileAttributes attributes)
    {
        string name = Path.GetFileName(relativePath);
        return !Exclude.Any(mask => Matches(mask, name, relativePath)) && !IsJunk(name) && !HasSkippedAttributes(attributes);
    }

    private bool IsJunk(string name) => SkipJunk && JunkMasks.Any(mask => FileSystemName.MatchesSimpleExpression(mask, name));

    private bool HasSkippedAttributes(FileAttributes attributes) =>
        (SkipHidden && (attributes & FileAttributes.Hidden) != 0) || (SkipSystem && (attributes & FileAttributes.System) != 0);

    private static bool Matches(string mask, string name, string relativePath)
    {
        string trimmed = mask.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return trimmed.Length > 0
            && (FileSystemName.MatchesSimpleExpression(trimmed, name) || FileSystemName.MatchesSimpleExpression(trimmed, relativePath));
    }
}
