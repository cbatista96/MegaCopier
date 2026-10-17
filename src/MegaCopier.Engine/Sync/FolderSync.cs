using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MegaCopier.Engine.Sync;

/// <summary>Qué hacer con un elemento al sincronizar dos carpetas.</summary>
public enum SyncAction
{
    /// <summary>Igual en los dos lados.</summary>
    Equal,
    CopyToRight,
    CopyToLeft,
    DeleteLeft,
    DeleteRight,
    CreateFolderLeft,
    CreateFolderRight,
    DeleteFolderLeft,
    DeleteFolderRight,

    /// <summary>Renombrado o movido en la izquierda: en la derecha se cambia de nombre (<see cref="SyncItem.MovedFrom"/>) en vez de copiarlo otra vez.</summary>
    MoveRight,

    /// <summary>Renombrado o movido en la derecha: lo mismo en la izquierda.</summary>
    MoveLeft,
}

/// <summary>Un archivo o carpeta de un lado: tamaño (-1 en carpetas) y fecha de modificación.</summary>
public readonly record struct SyncEntry(long Size, DateTime ModifiedUtc)
{
    public bool IsFolder => Size < 0;
}

/// <summary>Un elemento en la comparación: lo que hay en cada lado y qué se hará.</summary>
/// <param name="Relative">Ruta relativa (igual en los dos lados).</param>
/// <param name="Reason">Por qué (para la vista de comparación): "nuevo", "cambiado", "borrado", "los dos cambiaron"...</param>
public sealed record SyncItem(string Relative, SyncEntry? Left, SyncEntry? Right, SyncAction Action, SyncReason Reason)
{
    /// <summary>En <see cref="SyncAction.MoveRight"/> y <see cref="SyncAction.MoveLeft"/>: el nombre que tiene ahora en el lado que se renombra.</summary>
    public string? MovedFrom { get; init; }

    public bool IsFolder => (Left ?? Right)?.IsFolder == true;
}

public enum SyncReason
{
    Same,
    New,
    Changed,
    Deleted,

    /// <summary>Cambió en los dos lados desde la última vez (o es la primera vez y son distintos): gana el más reciente.</summary>
    BothChanged,

    /// <summary>Borrado en un lado pero cambiado en el otro: se conserva el cambio.</summary>
    ChangedAndDeleted,

    /// <summary>Espejo: el destino tiene algo que el origen no.</summary>
    Extra,

    /// <summary>Mismo tamaño y fecha que uno que ya no está con su nombre de antes: se renombró o se movió.</summary>
    Renamed,
}

/// <summary>
/// Comparar y sincronizar el contenido de dos carpetas, en dos sentidos o como espejo, con las reglas de
/// FreeFileSync. Para saber qué se borró en cada lado se recuerda el estado de la última sincronización (un .tsv por
/// pareja en la carpeta de datos). Sin recuerdo (la primera vez), lo distinto lo decide la fecha: gana el más reciente.
/// Nada se pierde: lo que se borra o se sobrescribe va a la Papelera (o, en USB y red, a .megacopier-borrados).
/// </summary>
public static class FolderSync
{
    public const string TrashFolder = ".megacopier-borrados";

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // los enlaces no se siguen
        ReturnSpecialDirectories = false,
    };

    /// <summary>Lo que hay en una carpeta, por ruta relativa (sin la papelera propia ni las copias a medias).</summary>
    public static Dictionary<string, SyncEntry> Scan(string root, FileFilter filter)
    {
        var result = new Dictionary<string, SyncEntry>(StringComparer.OrdinalIgnoreCase);
        var folder = new DirectoryInfo(root);
        if (!folder.Exists)
        {
            return result;
        }

        foreach (var info in folder.EnumerateFileSystemInfos("*", Walk))
        {
            string relative = Path.GetRelativePath(root, info.FullName);
            if (relative.StartsWith(TrashFolder, StringComparison.OrdinalIgnoreCase)
                || info.Name.EndsWith(FileTransfer.PartialSuffix, StringComparison.OrdinalIgnoreCase)
                || info.Name.EndsWith(FileTransfer.JournalSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (info is DirectoryInfo)
            {
                if (filter.AcceptsDirectory(relative, info.Attributes))
                {
                    result[relative] = new SyncEntry(-1, DateTime.MinValue);
                }
            }
            else if (info is FileInfo file && filter.AcceptsFile(relative, file))
            {
                result[relative] = new SyncEntry(file.Length, file.LastWriteTimeUtc);
            }
        }

        // Lo que está dentro de una carpeta excluida por el filtro (a cualquier profundidad), también fuera.
        foreach (string key in result.Keys.ToList())
        {
            for (string? parent = Path.GetDirectoryName(key); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            {
                if (!result.ContainsKey(parent))
                {
                    result.Remove(key);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>Qué hay que hacer para que las dos carpetas queden sincronizadas.</summary>
    /// <param name="last">Estado de la última sincronización en dos sentidos (vacío si nunca).</param>
    public static List<SyncItem> Plan(
        IReadOnlyDictionary<string, SyncEntry> left,
        IReadOnlyDictionary<string, SyncEntry> right,
        IReadOnlyDictionary<string, (SyncEntry Left, SyncEntry Right)> last,
        SyncDirection direction,
        TimeSpan tolerance,
        string? leftRoot = null,
        string? rightRoot = null)
    {
        bool Same(SyncEntry a, SyncEntry b) =>
            a.IsFolder == b.IsFolder && (a.IsFolder || (a.Size == b.Size && (a.ModifiedUtc - b.ModifiedUtc).Duration() <= tolerance));

        var items = new List<SyncItem>();
        var names = new SortedSet<string>(left.Keys.Concat(right.Keys), StringComparer.OrdinalIgnoreCase);
        foreach (string relative in names)
        {
            SyncEntry? l = left.TryGetValue(relative, out var a) ? a : null;
            SyncEntry? r = right.TryGetValue(relative, out var b) ? b : null;
            bool known = last.TryGetValue(relative, out var before);

            // Un archivo en un lado y una carpeta con el mismo nombre en el otro: no se toca (lo resuelve el usuario).
            if (l is { } lf && r is { } rf && lf.IsFolder != rf.IsFolder)
            {
                continue;
            }

            if (direction != SyncDirection.TwoWay)
            {
                bool toRight = direction == SyncDirection.LeftToRight;
                SyncEntry? from = toRight ? l : r;
                SyncEntry? to = toRight ? r : l;
                if (from is { } source && to is { } target)
                {
                    items.Add(Same(source, target) ? new(relative, l, r, SyncAction.Equal, SyncReason.Same)
                        : new(relative, l, r, toRight ? SyncAction.CopyToRight : SyncAction.CopyToLeft, SyncReason.Changed));
                }
                else if (from is { } only)
                {
                    items.Add(new(relative, l, r, only.IsFolder
                        ? (toRight ? SyncAction.CreateFolderRight : SyncAction.CreateFolderLeft)
                        : (toRight ? SyncAction.CopyToRight : SyncAction.CopyToLeft), SyncReason.New));
                }
                else if (to is { } extra)
                {
                    items.Add(new(relative, l, r, extra.IsFolder
                        ? (toRight ? SyncAction.DeleteFolderRight : SyncAction.DeleteFolderLeft)
                        : (toRight ? SyncAction.DeleteRight : SyncAction.DeleteLeft), SyncReason.Extra));
                }

                continue;
            }

            // Dos sentidos.
            if (l is { } left2 && r is { } right2)
            {
                if (Same(left2, right2))
                {
                    items.Add(new(relative, l, r, SyncAction.Equal, SyncReason.Same));
                    continue;
                }

                bool leftChanged = !known || !Same(left2, before.Left);
                bool rightChanged = !known || !Same(right2, before.Right);
                if (leftChanged && !rightChanged)
                {
                    items.Add(new(relative, l, r, SyncAction.CopyToRight, SyncReason.Changed));
                }
                else if (rightChanged && !leftChanged)
                {
                    items.Add(new(relative, l, r, SyncAction.CopyToLeft, SyncReason.Changed));
                }
                else
                {
                    // Los dos cambiaron (o no hay recuerdo): gana el más reciente; el otro va a la Papelera.
                    items.Add(new(relative, l, r, left2.ModifiedUtc >= right2.ModifiedUtc ? SyncAction.CopyToRight : SyncAction.CopyToLeft, SyncReason.BothChanged));
                }
            }
            else if (l is { } onlyLeft)
            {
                items.Add(OneSide(relative, l, r, onlyLeft, known ? before.Left : null, toRight: true));
            }
            else if (r is { } onlyRight)
            {
                items.Add(OneSide(relative, l, r, onlyRight, known ? before.Right : null, toRight: false));
            }
        }

        // Una carpeta borrada en un lado solo se borra en el otro si dentro no queda nada nuevo que conservar.
        var keptFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(i => i.Action is SyncAction.CopyToLeft or SyncAction.CopyToRight or SyncAction.CreateFolderLeft or SyncAction.CreateFolderRight))
        {
            for (string? parent = Path.GetDirectoryName(item.Relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            {
                keptFolders.Add(parent);
            }
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Action is SyncAction.DeleteFolderLeft or SyncAction.DeleteFolderRight && keptFolders.Contains(item.Relative))
            {
                bool deleteLeft = item.Action == SyncAction.DeleteFolderLeft;
                items[i] = item with { Action = deleteLeft ? SyncAction.CreateFolderRight : SyncAction.CreateFolderLeft, Reason = SyncReason.ChangedAndDeleted };
            }
        }

        if (leftRoot is not null && rightRoot is not null)
        {
            DetectMoves(items, tolerance, leftRoot, rightRoot);
        }
        return items;

        SyncItem OneSide(string relative, SyncEntry? l, SyncEntry? r, SyncEntry present, SyncEntry? before, bool toRight)
        {
            if (before is null)
            {
                // Nuevo en este lado.
                var create = present.IsFolder
                    ? (toRight ? SyncAction.CreateFolderRight : SyncAction.CreateFolderLeft)
                    : (toRight ? SyncAction.CopyToRight : SyncAction.CopyToLeft);
                return new(relative, l, r, create, SyncReason.New);
            }

            if (!present.IsFolder && !Same(present, before.Value))
            {
                // Borrado en el otro lado, pero aquí cambió después: se conserva el cambio.
                return new(relative, l, r, toRight ? SyncAction.CopyToRight : SyncAction.CopyToLeft, SyncReason.ChangedAndDeleted);
            }

            var delete = present.IsFolder
                ? (toRight ? SyncAction.DeleteFolderLeft : SyncAction.DeleteFolderRight)
                : (toRight ? SyncAction.DeleteLeft : SyncAction.DeleteRight);
            return new(relative, l, r, delete, SyncReason.Deleted);
        }
    }

    /// <summary>
    /// Renombrados y movidos: un archivo nuevo en un lado y uno que falta del otro, con el mismo tamaño y la misma
    /// fecha (renombrar no la cambia), son el mismo archivo. Entonces se renombra en el otro lado en vez de borrar uno y
    /// copiar el otro entero. Solo si la pareja es única: con dudas, se copia y se borra como siempre.
    /// </summary>
    private static void DetectMoves(List<SyncItem> items, TimeSpan tolerance, string leftRoot, string rightRoot)
    {
        foreach (bool toRight in new[] { true, false })
        {
            var copy = toRight ? SyncAction.CopyToRight : SyncAction.CopyToLeft;
            var delete = toRight ? SyncAction.DeleteRight : SyncAction.DeleteLeft;
            var added = items.Where(i => i.Action == copy && i.Reason == SyncReason.New && (toRight ? i.Left : i.Right) is { IsFolder: false, Size: > 0 }).ToList();
            var gone = items.Where(i => i.Action == delete && i.Reason is SyncReason.Deleted or SyncReason.Extra && (toRight ? i.Right : i.Left) is { IsFolder: false, Size: > 0 }).ToList();
            if (added.Count == 0 || gone.Count == 0)
            {
                continue;
            }

            var compared = new Dictionary<(SyncItem, SyncItem), bool>();

            bool Matches(SyncItem n, SyncItem g)
            {
                var a = (toRight ? n.Left : n.Right)!.Value;
                var b = (toRight ? g.Right : g.Left)!.Value;
                if (a.Size != b.Size || (a.ModifiedUtc - b.ModifiedUtc).Duration() > tolerance)
                {
                    return false;
                }

                // Mismo tamaño y fecha no basta (dos archivos distintos pueden coincidir): también el mismo contenido.
                if (!compared.TryGetValue((n, g), out bool same))
                {
                    compared[(n, g)] = same = SameContent(Path.Combine(toRight ? leftRoot : rightRoot, n.Relative), Path.Combine(toRight ? rightRoot : leftRoot, g.Relative));
                }

                return same;
            }

            foreach (var item in added)
            {
                var candidates = gone.Where(g => Matches(item, g)).Take(2).ToList();
                if (candidates.Count != 1 || added.Count(n => Matches(n, candidates[0])) != 1)
                {
                    continue; // varios iguales: no se sabe cuál es cuál
                }

                var old = candidates[0];
                gone.Remove(old);
                items.Remove(old);
                int index = items.IndexOf(item);
                items[index] = item with
                {
                    Action = toRight ? SyncAction.MoveRight : SyncAction.MoveLeft,
                    Reason = SyncReason.Renamed,
                    Left = toRight ? item.Left : old.Left,
                    Right = toRight ? old.Right : item.Right,
                    MovedFrom = old.Relative,
                };
            }
        }
    }

    private static bool SameContent(string a, string b)
    {
        try
        {
            using var first = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            using var second = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            if (first.Length != second.Length)
            {
                return false;
            }

            byte[] x = new byte[1 << 20], y = new byte[1 << 20];
            int read;
            while ((read = first.ReadAtLeast(x, x.Length, throwOnEndOfStream: false)) > 0)
            {
                if (second.ReadAtLeast(y, read, throwOnEndOfStream: false) != read || !x.AsSpan(0, read).SequenceEqual(y.AsSpan(0, read)))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // si no se puede leer, se copia como siempre
        }
    }

    // --- Recuerdo de la última sincronización ---

    public static string StateFile(string left, string right, string? folder = null)
    {
        string key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).ToUpperInvariant() + "|" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)).ToUpperInvariant();
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ".tsv";
        return Path.Combine(folder ?? Path.Combine(DataFolder.Path, "sync"), name);
    }

    public static Dictionary<string, (SyncEntry Left, SyncEntry Right)> LoadState(string file)
    {
        var state = new Dictionary<string, (SyncEntry, SyncEntry)>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(file))
        {
            return state;
        }

        foreach (string line in File.ReadLines(file, Encoding.UTF8))
        {
            string[] parts = line.Split('\t');
            if (parts.Length == 4
                && long.TryParse(parts[1], CultureInfo.InvariantCulture, out long size)
                && long.TryParse(parts[2], CultureInfo.InvariantCulture, out long leftTicks)
                && long.TryParse(parts[3], CultureInfo.InvariantCulture, out long rightTicks))
            {
                state[parts[0]] = (new SyncEntry(size, new DateTime(leftTicks, DateTimeKind.Utc)), new SyncEntry(size, new DateTime(rightTicks, DateTimeKind.Utc)));
            }
        }

        return state;
    }

    /// <summary>Guarda lo que quedó igual en los dos lados (lo que falló no se recuerda: la próxima vez se copia).</summary>
    public static void SaveState(string file, IReadOnlyDictionary<string, SyncEntry> left, IReadOnlyDictionary<string, SyncEntry> right, TimeSpan tolerance)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var text = new StringBuilder();
        foreach (var (relative, l) in left)
        {
            if (right.TryGetValue(relative, out var r) && l.IsFolder == r.IsFolder
                && (l.IsFolder || (l.Size == r.Size && (l.ModifiedUtc - r.ModifiedUtc).Duration() <= tolerance)))
            {
                text.Append(relative).Append('\t').Append(l.Size.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(l.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(r.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        string temp = file + ".tmp";
        File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
        File.Move(temp, file, overwrite: true);
    }

    // --- Borrar sin perder nada ---

    /// <summary>
    /// Quita un archivo o carpeta guardándolo: a la Papelera en discos fijos; en USB y red (no tienen Papelera) a
    /// &lt;raíz&gt;\.megacopier-borrados\&lt;fecha&gt;\ruta.
    /// </summary>
    public static void Discard(string path, string root, string stamp, bool recycleBin = true)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        // En modo de prueba nunca a la Papelera del usuario: todo a .megacopier-borrados.
        if (recycleBin && Environment.GetEnvironmentVariable("MEGACOPIER_TEST") != "1" && HasRecycleBin(root) && Native.Recycle.ToRecycleBin(path))
        {
            return;
        }

        string target = Path.Combine(root, TrashFolder, stamp, Path.GetRelativePath(root, path));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var hidden = new DirectoryInfo(Path.Combine(root, TrashFolder));
        hidden.Attributes |= FileAttributes.Hidden;
        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target, overwrite: true);
        }
    }

    private static bool HasRecycleBin(string root)
    {
        try
        {
            string? drive = Path.GetPathRoot(Path.GetFullPath(root));
            return drive is { Length: > 0 } && !drive.StartsWith(@"\\", StringComparison.Ordinal) && new DriveInfo(drive).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
