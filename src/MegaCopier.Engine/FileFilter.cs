using System.IO.Enumeration;

namespace MegaCopier.Engine;

/// <summary>
/// Qué archivos entran en la copia: máscaras de inclusión y exclusión
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

    /// <summary>
    /// Omitir los archivos que solo están en la nube (OneDrive, Dropbox, Google Drive en «solo en línea»): copiarlos los
    /// descargaría antes, quizá gigas que no se querían bajar.
    /// </summary>
    public bool SkipCloudOnly { get; set; }

    /// <summary>Omitir archivos "basura" que crean Windows, macOS y Office (<see cref="JunkMasks"/>).</summary>
    public bool SkipJunk { get; set; }

    /// <summary>Miniaturas, configuración de carpetas, metadatos de macOS y archivos de bloqueo de Office.</summary>
    public static readonly string[] JunkMasks = ["Thumbs.db", "ehthumbs.db", "desktop.ini", ".DS_Store", "._*", "~$*", "$RECYCLE.BIN", "System Volume Information"];

    /// <summary>Solo los archivos con alguno de estos atributos (por ejemplo, «Archivo»: lo modificado desde la última copia).</summary>
    public FileAttributes RequireAttributes { get; set; }

    /// <summary>Ningún archivo con alguno de estos atributos (comprimido, cifrado, temporal, sin conexión…).</summary>
    public FileAttributes SkipAttributes { get; set; }

    /// <summary>Usados (abiertos) por última vez desde / hasta esta fecha. Windows puede no anotar cada apertura.</summary>
    public DateTime? AccessedAfterUtc { get; set; }

    public DateTime? AccessedBeforeUtc { get; set; }

    /// <summary>Cuántos niveles de subcarpetas se recorren: 0 = solo lo que hay directamente en el origen; null = todos.</summary>
    public int? MaxDepth { get; set; }

    public bool IsEmpty => Include.Count == 0 && Exclude.Count == 0 && MinSize is null && MaxSize is null
        && ModifiedAfterUtc is null && ModifiedBeforeUtc is null && !SkipHidden && !SkipSystem && !SkipCloudOnly && !SkipJunk && ImageText.Length == 0
        && RequireAttributes == 0 && SkipAttributes == 0 && AccessedAfterUtc is null && AccessedBeforeUtc is null && MaxDepth is null;

    public bool AcceptsFile(string relativePath, FileInfo file) =>
        AcceptsFile(relativePath, file.Length, file.LastWriteTimeUtc) && !HasSkippedAttributes(file.Attributes)
        && !(SkipCloudOnly && IsCloudOnly(file.Attributes)) && AcceptsAttributes(file.Attributes) && AcceptsAccess(file.LastAccessTimeUtc)
        && HasImageText(file);

    private bool AcceptsAttributes(FileAttributes attributes) =>
        (RequireAttributes == 0 || (attributes & RequireAttributes) != 0) && (attributes & SkipAttributes) == 0;

    private bool AcceptsAccess(DateTime accessed) =>
        !((AccessedAfterUtc is { } after && accessed < after) || (AccessedBeforeUtc is { } before && accessed > before));

    /// <summary>Niveles de carpeta de una ruta relativa al origen («a\b\c.txt» está en el nivel 2).</summary>
    private static int Depth(string relativePath) => relativePath.Count(c => c == Path.DirectorySeparatorChar);

    /// <summary>
    /// El archivo no está en el disco: es un marcador de la nube que se descarga al leerlo (FILE_ATTRIBUTE_OFFLINE,
    /// RECALL_ON_OPEN o RECALL_ON_DATA_ACCESS, los de OneDrive y los demás proveedores de Windows).
    /// </summary>
    public static bool IsCloudOnly(FileAttributes attributes) =>
        ((int)attributes & (0x1000 | 0x40000 | 0x400000)) != 0;

    /// <summary>Solo fotos y capturas que contengan este texto (leído con el OCR de Windows, sin internet).</summary>
    public string ImageText { get; set; } = "";

    /// <summary>
    /// Lee el texto de una imagen (la app lo conecta al OCR de Windows; null = no se pudo). Sin él, el filtro de texto
    /// no deja pasar nada: mejor no copiar que copiar lo que no se pidió.
    /// </summary>
    public static Func<string, string?>? ImageTextReader { get; set; }

    private static readonly HashSet<string> OcrImages = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".webp", ".jxr" };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, long Length, DateTime Modified), string> OcrCache = new();

    private bool HasImageText(FileInfo file)
    {
        if (ImageText.Length == 0)
        {
            return true;
        }

        if (!OcrImages.Contains(file.Extension) || ImageTextReader is not { } reader)
        {
            return false;
        }

        var key = (file.FullName, file.Length, file.LastWriteTimeUtc);
        if (!OcrCache.TryGetValue(key, out string? text))
        {
            text = Fold(reader(file.FullName) ?? "");
            OcrCache[key] = text;
        }

        return text.Contains(Fold(ImageText), StringComparison.Ordinal);
    }

    /// <summary>Sin mayúsculas, acentos ni espacios repetidos: el OCR no siempre los acierta.</summary>
    private static string Fold(string text)
    {
        var builder = new System.Text.StringBuilder();
        bool space = false;
        foreach (char c in text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Lo mismo para un archivo que no está en el disco (un móvil): sin atributos y quizá sin fecha.</summary>
    public bool AcceptsFile(string relativePath, long length, DateTime? lastWriteUtc)
    {
        string name = Path.GetFileName(relativePath);
        if (Exclude.Any(mask => Matches(mask, name, relativePath)) || IsJunk(name) || (MaxDepth is { } maxDepth && Depth(relativePath) > maxDepth))
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
        // Una carpeta del nivel «a\b» es la profundidad 2: con MaxDepth 1 ya no se entra.
        return !Exclude.Any(mask => Matches(mask, name, relativePath)) && !IsJunk(name) && !HasSkippedAttributes(attributes)
            && !(MaxDepth is { } maxDepth && Depth(relativePath) + 1 > maxDepth);
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
