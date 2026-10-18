using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MegaCopier.App.Services;

/// <summary>
/// Enseña cosas en el Explorador: una carpeta abierta (su contenido) o uno o varios elementos marcados dentro de la
/// carpeta que los contiene, como «Mostrar en la carpeta». En modo de prueba solo deja constancia (no abre ventanas).
/// </summary>
internal static partial class ShellReveal
{
    /// <summary>Abre la carpeta para ver lo que tiene dentro.</summary>
    public static void Open(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        if (App.TestMode)
        {
            App.TestLog("explorer.log", $"abrir {folder}");
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Abre la carpeta que contiene los elementos con ellos marcados. Los que no existen se omiten; si no queda
    /// ninguno, se abre la carpeta (o la primera que exista hacia arriba).
    /// </summary>
    public static void Select(IReadOnlyList<string> paths)
    {
        var existing = paths.Select(p => Path.TrimEndingDirectorySeparator(p)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existing.Count == 0)
        {
            string? folder = paths.Count > 0 ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(paths[0])) : null;
            while (folder is not null && !Directory.Exists(folder))
            {
                folder = Path.GetDirectoryName(folder);
            }

            if (folder is not null)
            {
                Open(folder);
            }

            return;
        }

        // Una unidad entera (E:\) no tiene carpeta donde marcarla: se abre.
        if (existing.Count == 1 && Path.GetPathRoot(existing[0]) is { } root && string.Equals(Path.TrimEndingDirectorySeparator(root), existing[0], StringComparison.OrdinalIgnoreCase))
        {
            Open(existing[0] + "\\");
            return;
        }

        string parent = Path.GetDirectoryName(existing[0])!;
        var items = existing.Where(p => string.Equals(Path.GetDirectoryName(p), parent, StringComparison.OrdinalIgnoreCase)).ToList();
        if (App.TestMode)
        {
            App.TestLog("explorer.log", $"marcar {string.Join(" | ", items)}");
            return;
        }

        if (!SelectInFolder(parent, items))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{items[0]}\"") { UseShellExecute = true });
        }
    }

    /// <summary>SHOpenFolderAndSelectItems: marca varios elementos a la vez (explorer /select solo admite uno).</summary>
    private static bool SelectInFolder(string folder, List<string> items)
    {
        nint folderId = ILCreateFromPathW(folder);
        if (folderId == 0)
        {
            return false;
        }

        var ids = new nint[items.Count];
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                ids[i] = ILCreateFromPathW(items[i]);
                if (ids[i] == 0)
                {
                    return false;
                }
            }

            return SHOpenFolderAndSelectItems(folderId, (uint)ids.Length, ids, 0) >= 0;
        }
        finally
        {
            foreach (nint id in ids.Where(i => i != 0))
            {
                ILFree(id);
            }

            ILFree(folderId);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ILCreateFromPathW(string path);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(nint id);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(nint folder, uint count, [In] nint[] items, uint flags);
}
