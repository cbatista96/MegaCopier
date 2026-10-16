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

    public bool IsEmpty => Include.Count == 0 && Exclude.Count == 0 && MinSize is null && MaxSize is null
        && ModifiedAfterUtc is null && ModifiedBeforeUtc is null && !SkipHidden && !SkipSystem;

    public bool AcceptsFile(string relativePath, FileInfo file)
    {
        string name = Path.GetFileName(relativePath);
        if (Exclude.Any(mask => Matches(mask, name, relativePath)))
        {
            return false;
        }

        if (Include.Count > 0 && !Include.Any(mask => Matches(mask, name, relativePath)))
        {
            return false;
        }

        if ((MinSize is { } min && file.Length < min) || (MaxSize is { } max && file.Length > max))
        {
            return false;
        }

        if ((ModifiedAfterUtc is { } after && file.LastWriteTimeUtc < after) || (ModifiedBeforeUtc is { } before && file.LastWriteTimeUtc > before))
        {
            return false;
        }

        return !HasSkippedAttributes(file.Attributes);
    }

    public bool AcceptsDirectory(string relativePath, FileAttributes attributes)
    {
        string name = Path.GetFileName(relativePath);
        return !Exclude.Any(mask => Matches(mask, name, relativePath)) && !HasSkippedAttributes(attributes);
    }

    private bool HasSkippedAttributes(FileAttributes attributes) =>
        (SkipHidden && (attributes & FileAttributes.Hidden) != 0) || (SkipSystem && (attributes & FileAttributes.System) != 0);

    private static bool Matches(string mask, string name, string relativePath)
    {
        string trimmed = mask.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return trimmed.Length > 0
            && (FileSystemName.MatchesSimpleExpression(trimmed, name) || FileSystemName.MatchesSimpleExpression(trimmed, relativePath));
    }
}
