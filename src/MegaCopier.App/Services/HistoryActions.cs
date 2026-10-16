using System.Runtime.InteropServices;
using MegaCopier.Engine.Ipc;
using Microsoft.UI.Xaml;

namespace MegaCopier.App.Services;

/// <summary>Resultado de deshacer una copia.</summary>
public readonly record struct UndoResult(int Removed, int Changed, int Replaced, int Missing);

/// <summary>"Deshacer" y "Copiar de vuelta" desde el Historial.</summary>
internal static partial class HistoryActions
{
    /// <summary>Se puede deshacer: copias con su lista de archivos guardada, o movimientos (se devuelven a su sitio).</summary>
    public static bool CanUndo(HistoryEntry entry) =>
        !entry.Undone && entry.Operation == Engine.JobOperation.Copy && entry.Files > 0
        && (entry.Move || History.HasFiles(entry.Id));

    /// <summary>
    /// Deshace una copia: manda a la Papelera los archivos que copió (se pueden recuperar), salvo los que cambiaron
    /// después o los que reemplazaron a otro archivo (borrarlos no devolvería el anterior). Quita las carpetas que queden
    /// vacías. Un movimiento se deshace devolviendo todo a su carpeta original.
    /// </summary>
    public static UndoResult Undo(HistoryEntry entry)
    {
        if (entry.Move)
        {
            Reverse(entry, move: true);
            History.MarkUndone(entry.Id);
            return new UndoResult(entry.Files, 0, 0, 0);
        }

        int changed = 0, replaced = 0, missing = 0;
        var remove = new List<string>();
        foreach (var file in History.Files(entry.Id))
        {
            var info = new FileInfo(file.Destination);
            if (file.Replaced)
            {
                replaced++;
            }
            else if (!info.Exists)
            {
                missing++;
            }
            else if (info.Length != file.Length || info.LastWriteTimeUtc != file.LastWriteUtc)
            {
                changed++;
            }
            else
            {
                remove.Add(file.Destination);
            }
        }

        int removed = Recycle(remove);
        RemoveEmptyFolders(remove, entry.Destination);
        History.MarkUndone(entry.Id);
        return new UndoResult(removed, changed, replaced, missing);
    }

    /// <summary>
    /// La copia al revés: lo que está en el destino vuelve a la carpeta de donde salió (restaurar desde una copia de
    /// seguridad). Si los orígenes venían de varias carpetas, una copia por carpeta.
    /// </summary>
    public static int Reverse(HistoryEntry entry, bool move)
    {
        var app = (App)Application.Current;
        int jobs = 0;
        foreach (var group in entry.Sources.GroupBy(s => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(s)) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            var back = group
                .Select(s => Path.Combine(entry.Destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(s))))
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToList();
            if (back.Count == 0 || group.Key.Length == 0)
            {
                continue;
            }

            app.Submit(new IpcRequest { Command = "copy", Sources = back, Destination = group.Key, Move = move ? true : null });
            jobs++;
        }

        return jobs;
    }

    private static void RemoveEmptyFolders(IEnumerable<string> files, string root)
    {
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        foreach (string folder in files.Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(f => f.Length))
        {
            string? current = folder;
            while (current is not null && current.Length > top.Length && current.StartsWith(top, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (Directory.EnumerateFileSystemEntries(current).Any())
                    {
                        break;
                    }

                    Directory.Delete(current);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }
        }
    }

    /// <summary>Envía archivos a la Papelera de reciclaje (SHFileOperation con FOF_ALLOWUNDO), en tandas.</summary>
    private static unsafe int Recycle(List<string> paths)
    {
        int done = 0;
        foreach (var chunk in paths.Chunk(500))
        {
            // Lista de rutas separadas por \0 y terminada en \0\0.
            string list = string.Join('\0', chunk) + "\0\0";
            fixed (char* from = list)
            {
                var operation = new FileOperation
                {
                    Function = 3,                          // FO_DELETE
                    From = from,
                    Flags = 0x0040 | 0x0010 | 0x0004 | 0x0400, // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
                };
                SHFileOperationW(&operation);
                done += chunk.Count(p => !File.Exists(p));
            }
        }

        return done;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct FileOperation
    {
        public nint Window;
        public uint Function;
        public char* From;
        public char* To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public nint NameMappings;
        public char* ProgressTitle;
    }

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHFileOperationW(FileOperation* operation);
}
