namespace MegaCopier.Shell;

/// <summary>Un arrastre con el botón izquierdo entre ventanas del Explorador que MegaCopier se queda.</summary>
public sealed record DragRequest(IReadOnlyList<string> Sources, string Destination, bool Move);

/// <summary>Qué hay bajo el puntero al soltar.</summary>
public enum DropSpot
{
    /// <summary>No se sabe (otra ventana encima, un control raro...): mejor que suelte el Explorador.</summary>
    Unknown,

    /// <summary>El fondo de la lista de archivos: se suelta en la carpeta que muestra la ventana.</summary>
    Background,

    /// <summary>Un elemento de la lista (carpeta, archivo, ZIP, programa...).</summary>
    Item,

    /// <summary>
    /// Una carpeta del panel de navegación (o de otro sitio que ya da su ruta): se suelta dentro de ella. Su ruta llega
    /// en la carpeta de destino.
    /// </summary>
    Folder,
}

/// <summary>Un elemento de la carpeta de destino tal como lo ve la vista del Explorador.</summary>
public sealed record ViewItem(string DisplayName, string? Path, bool IsFolder);

/// <summary>
/// Decide, con las mismas reglas que el Explorador, si un arrastre lo hace MegaCopier y con qué operación.
/// Ante cualquier duda devuelve un motivo para que lo haga el Explorador: un arrastre mal interpretado
/// podría mover archivos a un sitio inesperado.
/// </summary>
public static class DragDecision
{
    /// <param name="sources">Rutas seleccionadas en la ventana de origen (null si alguna no es del disco).</param>
    /// <param name="sourceFolder">Carpeta que muestra la ventana de origen (null si es virtual: búsqueda, Inicio...).</param>
    /// <param name="viewFolder">Carpeta que muestra la ventana de destino (null si es virtual), o la carpeta del panel de navegación sobre la que se suelta.</param>
    /// <param name="spot">Qué hay bajo el puntero.</param>
    /// <param name="itemName">Nombre visible del elemento bajo el puntero (si <paramref name="spot"/> es Item).</param>
    /// <param name="viewItems">Elementos de la carpeta de destino, para traducir ese nombre a una ruta.</param>
    /// <param name="ctrl">Ctrl pulsado al soltar (forzar copia).</param>
    /// <param name="shift">Mayúsculas pulsado al soltar (forzar mover).</param>
    /// <param name="alt">Alt pulsado al soltar (crear acceso directo).</param>
    /// <param name="request">La copia para MegaCopier, si el resultado es null.</param>
    /// <returns>Null si MegaCopier se queda el arrastre; si no, por qué lo hace el Explorador.</returns>
    public static string? Decide(
        IReadOnlyList<string>? sources,
        string? sourceFolder,
        string? viewFolder,
        DropSpot spot,
        string? itemName,
        IReadOnlyList<ViewItem> viewItems,
        bool ctrl,
        bool shift,
        bool alt,
        out DragRequest? request)
    {
        request = null;
        if (alt || (ctrl && shift))
        {
            return "Alt o Ctrl+Mayúsculas: acceso directo";
        }

        if (sources is null || sources.Count == 0)
        {
            return "el origen no son archivos del disco";
        }

        string destination;
        switch (spot)
        {
            case DropSpot.Background:
                if (viewFolder is null)
                {
                    return "la carpeta de destino es virtual";
                }

                destination = viewFolder;
                break;
            case DropSpot.Item:
                // Hay que saber exactamente sobre qué se suelta: un archivo, un ZIP o un programa reciben el arrastre ellos.
                // Vale también en vistas virtuales (Este equipo, Inicio): sus unidades y carpetas tienen ruta.
                var matches = viewItems.Where(i => string.Equals(i.DisplayName, itemName, StringComparison.CurrentCultureIgnoreCase)).ToList();
                if (matches.Count != 1)
                {
                    return matches.Count == 0 ? "no se encontró el elemento bajo el puntero" : "hay varios elementos con ese nombre";
                }

                if (!matches[0].IsFolder || matches[0].Path is null)
                {
                    return "se suelta sobre un archivo, un ZIP o un programa";
                }

                destination = matches[0].Path!;
                break;
            case DropSpot.Folder:
                if (viewFolder is null)
                {
                    return "la carpeta del panel de navegación no es del disco";
                }

                destination = viewFolder;
                break;
            default:
                return "no se sabe qué hay bajo el puntero";
        }

        // Soltar en la carpeta de la que vienen no hace nada (en una búsqueda o en Inicio el origen no es una carpeta:
        // se mira la de cada elemento).
        destination = Path.TrimEndingDirectorySeparator(destination);
        bool sameFolder = sourceFolder is not null
            ? string.Equals(destination, Path.TrimEndingDirectorySeparator(sourceFolder), StringComparison.OrdinalIgnoreCase)
            : sources.All(s => string.Equals(destination, Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(s)) ?? ""), StringComparison.OrdinalIgnoreCase));
        if (sameFolder)
        {
            return "se suelta en la misma carpeta";
        }

        foreach (string source in sources)
        {
            string trimmed = Path.TrimEndingDirectorySeparator(source);
            if (string.Equals(destination, trimmed, StringComparison.OrdinalIgnoreCase)
                || destination.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "una carpeta dentro de sí misma";
            }

            if (!ctrl && !shift && string.Equals(Path.GetExtension(trimmed), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return "arrastrar un programa crea un acceso directo";
            }
        }

        // Como el Explorador: en la misma unidad se mueve; entre unidades se copia. Ctrl copia y Mayúsculas mueve.
        bool move = shift || (!ctrl && SameVolume(sources[0], destination));
        request = new DragRequest(sources, destination, move);
        return null;
    }

    public static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(a)?.TrimEnd('\\'), Path.GetPathRoot(b)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
