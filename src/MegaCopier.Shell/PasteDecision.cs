namespace MegaCopier.Shell;

/// <summary>Las mismas reglas para teclado, menús y barras del Explorador, sin consultar el disco desde un gancho.</summary>
public static class PasteDecision
{
    /// <summary>Null significa atendido. Sin petición es un movimiento sobre su propia carpeta: no hace nada.</summary>
    public static string? Decide(ClipboardFileList? clipboard, string? destination, out PasteRequest? request)
    {
        request = null;
        if (clipboard is null || clipboard.Paths.Count == 0) return "el portapapeles no contiene archivos";
        if (string.IsNullOrEmpty(destination) || !Path.IsPathFullyQualified(destination))
            return "el destino no es una carpeta del disco";

        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (clipboard.Paths.Any(p => !Path.IsPathFullyQualified(p))) return "el origen no es una ruta del disco";
        var sources = clipboard.Paths.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        bool SameParent(string path) => string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);

        // Al cortar y pegar en el mismo sitio no se borra, no se duplica y no se vacía el portapapeles.
        if (clipboard.Operation == ClipboardOperation.Move) sources = sources.Where(p => !SameParent(p)).ToArray();
        if (sources.Length > 0)
        {
            request = new PasteRequest(sources, folder, clipboard.Operation)
            {
                DuplicateInPlace = clipboard.Operation == ClipboardOperation.Copy && sources.Any(SameParent),
                ClipboardSources = clipboard.Paths,
            };
        }
        return null;
    }

    internal static string? ContextDestination(ViewSnapshot? snapshot, DropSpot spot, string? itemName) => spot switch
    {
        DropSpot.Background => snapshot?.Folder,
        DropSpot.Item => ItemFolder(snapshot?.Items ?? [], itemName),
        _ => null,
    };

    private static string? ItemFolder(IReadOnlyList<ViewItem> items, string? name)
    {
        var matches = items.Where(i => string.Equals(i.DisplayName, name, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        return matches.Length == 1 && matches[0].IsFolder ? matches[0].Path : null;
    }
}
