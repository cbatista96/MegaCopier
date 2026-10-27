namespace MegaCopier.Engine;

internal sealed record PlannedFile(string Source, string Destination, long Length, DateTime LastWriteUtc);

internal sealed record PlannedDirectory(string Source, string Destination, int Depth);

/// <summary>Un enlace simbólico o una unión de carpetas que se recrea en el destino apuntando al mismo sitio.</summary>
internal sealed record PlannedLink(string Source, string Destination, string Target, bool IsDirectory);

/// <summary>Nombre que recibe un origen en el destino (y con el que se muestra): el suyo o, si es una unidad entera, el de la unidad.</summary>
public static class SourceNames
{
    public static string TargetName(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return "";
        }

        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        try
        {
            return name.Length == 0 || CopyPlanner.IsDriveRoot(source) ? CopyPlanner.DriveFolderName(source) : name;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return name;
        }
    }
}

internal sealed record CopyPlan(
    IReadOnlyList<PlannedFile> Files,
    IReadOnlyList<PlannedDirectory> Directories,
    IReadOnlyList<PlannedLink> Links,
    IReadOnlyList<string> Warnings)
{
    public long TotalBytes { get; } = Files.Sum(f => f.Length);
}

/// <summary>
/// Convierte la lista de orígenes en la lista concreta de archivos, carpetas y enlaces a crear. Recorre las carpetas a
/// mano para decidir qué hacer con cada enlace: los "reparse points" que no son enlaces (archivos de OneDrive en la
/// nube, deduplicación...) se copian como archivos normales.
/// </summary>
internal static class CopyPlanner
{
    private static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Lo que Windows guarda en la raíz de cada unidad y no es del usuario: al copiar una unidad entera no se copia
    /// (como Robocopy con «System Volume Information»). Solo se mira en la raíz.
    /// </summary>
    private static readonly HashSet<string> DriveSystemEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$Recycle.Bin", "$RECYCLE.BIN", "RECYCLER", "pagefile.sys", "hiberfil.sys",
        "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp", "$WinREAgent", "$SysReset", "Config.Msi",
    };

    /// <summary>La ruta es la raíz de una unidad ("E:\") o de un recurso de red ("\\servidor\recurso").</summary>
    public static bool IsDriveRoot(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        return root is not null && string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Carpeta que recibe una unidad entera: su nombre y su letra («Juegos SSD 500GB (E)»), o «Unidad E» si no tiene
    /// nombre. Así su contenido nunca se mezcla con lo que ya hay en el destino.
    /// </summary>
    public static string DriveFolderName(string root)
    {
        string full = Path.GetFullPath(root);
        string trimmed = Path.TrimEndingDirectorySeparator(full).TrimEnd(Path.DirectorySeparatorChar); // "E:\" → "E:"
        string letter = trimmed.Length == 2 && trimmed[1] == ':' ? trimmed[..1].ToUpperInvariant() : "";
        string label = "";
        try
        {
            label = new DriveInfo(trimmed + Path.DirectorySeparatorChar).VolumeLabel;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Sin nombre (o una unidad de red): se usa la letra.
        }

        if (letter.Length == 0)
        {
            // \\servidor\recurso: el nombre del recurso.
            letter = trimmed.TrimStart('\\').Replace('\\', ' ');
        }

        string name = label.Trim().Length > 0 ? $"{label.Trim()} ({letter})" : Texts.F("E_DriveFolder", letter);
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.TrimEnd(' ', '.');
    }

    /// <param name="allowDriveRoot">
    /// Se puede elegir una unidad entera como origen (copiar, mover, comprobar). Borrar lo prohíbe (ver RunDelete).
    /// </param>
    public static CopyPlan Build(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        FileFilter? filter = null,
        LinkHandling links = LinkHandling.CopyLink,
        IReadOnlyDictionary<string, string>? targetNames = null,
        bool backupMode = false,
        bool allowDriveRoot = true)
    {
        filter = filter is { IsEmpty: false } ? filter : null;
        if (sources.Count == 0)
        {
            throw new CopyPlanException(Texts.Get("E_NoSources"));
        }

        string destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        var limits = DestinationNames.Limits.For(destinationRoot);
        var walk = new Walk(filter, links, backupMode, limits);

        try
        {
            foreach (string raw in sources)
            {
                string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
                string name = Path.GetFileName(source);
                bool driveRoot = string.IsNullOrEmpty(name) || IsDriveRoot(source);
                if (driveRoot)
                {
                    if (!allowDriveRoot)
                    {
                        throw new CopyPlanException(Texts.F("E_WholeDrive", raw));
                    }

                    source = Path.GetPathRoot(Path.GetFullPath(raw))!;
                    name = DriveFolderName(source);
                }

                if (targetNames is not null && targetNames.TryGetValue(source, out string? renamed))
                {
                    if (string.IsNullOrWhiteSpace(renamed) || renamed != Path.GetFileName(renamed) || renamed is "." or "..")
                        throw new ArgumentException("Invalid duplicate name", nameof(targetNames));
                    name = renamed;
                }
                else if (!driveRoot && DestinationNames.Fix(name, limits) is var fixedName && fixedName != name)
                {
                    walk.Warnings.Add(Texts.F("E_NameFixed", name, fixedName));
                    name = fixedName;
                }

                string target = Path.Combine(destinationRoot, name);
                FileSystemInfo info = Directory.Exists(source) ? new DirectoryInfo(source) : new FileInfo(source);
                if (!info.Exists && info.LinkTarget is null)
                {
                    throw new CopyPlanException(Texts.F("E_SourceMissing", raw));
                }

                if (info is DirectoryInfo && IsSameOrInside(target, source))
                {
                    throw new CopyPlanException(Texts.F("E_IntoItself", source));
                }

                // Un origen elegido a mano se copia siempre, aunque los filtros no lo aceptaran.
                walk.Entry(info, target, relative: "", depth: 0, explicitSource: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CopyPlanException(Texts.F("E_SourceUnreadable", ex.Message), ex);
        }

        if (walk.CloudOnly > 0)
        {
            walk.Warnings.Add(Texts.F("E_CloudOnly", walk.CloudOnly));
        }

        return new CopyPlan(walk.Files, walk.Directories, walk.Links, walk.Warnings);
    }

    public static bool IsTransferArtifact(string fileName) =>
        fileName.EndsWith(FileTransfer.PartialSuffix, StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(FileTransfer.JournalSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); // "E:\" ya lleva la barra

    /// <summary>
    /// Ruta literal (\\?\) para un origen con algún nombre que acaba en espacio o punto: sin el prefijo, Windows quita ese
    /// final al abrirlo y leería otro archivo («nota.txt » se abriría como «nota.txt»).
    /// </summary>
    private static string Verbatim(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        foreach (string part in path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is not "." and not ".." && (part.EndsWith(' ') || part.EndsWith('.')))
            {
                return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
            }
        }

        return path;
    }

    private sealed class Walk(FileFilter? filter, LinkHandling links, bool backupMode, DestinationNames.Limits limits)
    {
        /// <summary>
        /// El nombre en el destino: el mismo si el destino lo admite; si no, arreglado y, si así coincidiera con otro de
        /// la misma carpeta, numerado («nombre (2).ext»). Cada cambio queda en el registro.
        /// </summary>
        private string TargetName(string name, List<FileSystemInfo> siblings, ref HashSet<string>? taken)
        {
            string fixedName = DestinationNames.Fix(name, limits);
            if (fixedName == name)
            {
                return name;
            }

            taken ??= new HashSet<string>(siblings.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            string candidate = fixedName;
            for (int number = 2; taken.Contains(candidate); number++)
            {
                candidate = DestinationNames.Numbered(fixedName, number, limits);
            }

            taken.Add(candidate);
            Warnings.Add(Texts.F("E_NameFixed", name, candidate));
            return candidate;
        }

        /// <summary>
        /// Carpetas reales del camino actual: un enlace que apunta a una de ellas (su padre o un abuelo) haría un bucle
        /// infinito. Llegar a la misma carpeta por dos caminos distintos no es un bucle y se copia dos veces.
        /// </summary>
        private readonly HashSet<string> _ancestors = new(StringComparer.OrdinalIgnoreCase);

        public List<PlannedFile> Files { get; } = [];

        public List<PlannedDirectory> Directories { get; } = [];

        public List<PlannedLink> Links { get; } = [];

        public List<string> Warnings { get; } = [];

        /// <summary>Archivos que solo están en la nube y se descargarán al copiarlos.</summary>
        public int CloudOnly { get; set; }

        public void Entry(FileSystemInfo info, string target, string relative, int depth, bool explicitSource = false)
        {
            bool isDirectory = info is DirectoryInfo;
            if (!explicitSource && filter is not null)
            {
                bool accepted = isDirectory
                    ? filter.AcceptsDirectory(relative, info.Attributes)
                    : filter.AcceptsFile(relative, (FileInfo)info);
                if (!accepted)
                {
                    return;
                }
            }

            if (!isDirectory && IsTransferArtifact(info.Name))
            {
                return;
            }

            // Solo los enlaces simbólicos y las uniones tienen destino; otros "reparse points" son archivos normales.
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is { } linkTarget)
            {
                switch (links)
                {
                    case LinkHandling.Skip:
                        Warnings.Add(Texts.F("E_LinkSkipped", info.FullName));
                        return;
                    case LinkHandling.CopyLink:
                        Links.Add(new PlannedLink(info.FullName, target, linkTarget, isDirectory));
                        return;
                }

                // FollowTarget: se copia lo que hay detrás del enlace.
                if (!isDirectory)
                {
                    var resolved = new FileInfo(info.FullName).ResolveLinkTarget(returnFinalTarget: true) as FileInfo;
                    if (resolved is not { Exists: true })
                    {
                        Warnings.Add(Texts.F("E_BrokenLinkSkipped", info.FullName, linkTarget));
                        return;
                    }

                    Files.Add(new PlannedFile(Verbatim(info.FullName), target, resolved.Length, resolved.LastWriteTimeUtc));
                    return;
                }
            }

            if (!isDirectory)
            {
                var file = (FileInfo)info;
                Files.Add(new PlannedFile(Verbatim(file.FullName), target, file.Length, file.LastWriteTimeUtc));
                if (FileFilter.IsCloudOnly(file.Attributes))
                {
                    CloudOnly++;
                }
                return;
            }

            string real = RealPath((DirectoryInfo)info);
            if (!_ancestors.Add(real))
            {
                Warnings.Add(Texts.F("E_LinkLoop", info.FullName));
                return;
            }

            Directories.Add(new PlannedDirectory(Verbatim(info.FullName), target, depth));
            string verbatim = Verbatim(info.FullName);
            var children = Children(verbatim == info.FullName ? (DirectoryInfo)info : new DirectoryInfo(verbatim)).ToList();
            HashSet<string>? taken = null; // nombres de la carpeta de destino, solo si alguno hay que arreglarlo

            // Un .mcignore en la carpeta dice qué no se copia nunca de ella ni de sus subcarpetas (como .gitignore).
            bool ignoring = children.Any(c => c is FileInfo && string.Equals(c.Name, IgnoreFile.Name, StringComparison.OrdinalIgnoreCase))
                && IgnoreFile.Read(Path.Combine(verbatim, IgnoreFile.Name)) is { Count: > 0 } masks
                && _ignores.Add(verbatim, masks);
            foreach (var child in children)
            {
                if (depth == 0 && DriveSystemEntries.Contains(child.Name) && IsDriveRoot(info.FullName))
                {
                    continue; // lo de Windows en la raíz de una unidad entera
                }

                if (_ignores.Count > 0 && IgnoreFile.Ignored(_ignores, Path.Combine(verbatim, child.Name), child is DirectoryInfo))
                {
                    continue;
                }

                string childRelative = relative.Length == 0 ? child.Name : Path.Combine(relative, child.Name);
                Entry(child, Path.Combine(target, TargetName(child.Name, children, ref taken)), childRelative, depth + 1);
            }

            if (ignoring)
            {
                _ignores.RemoveLast();
            }

            _ancestors.Remove(real);
        }

        /// <summary>Los .mcignore de las carpetas que se están recorriendo (de la más alta a la actual).</summary>
        private readonly IgnoreFile.Stack _ignores = new();

        private IEnumerable<FileSystemInfo> Children(DirectoryInfo directory)
        {
            if (!backupMode) return directory.EnumerateFileSystemInfos("*", OneLevel);
            try { return directory.EnumerateFileSystemInfos("*", OneLevel).ToArray(); }
            catch (UnauthorizedAccessException) when (backupMode)
            {
                return Native.BackupPrivileges.EnumerateDirectory(directory.FullName);
            }
        }

        private static string RealPath(DirectoryInfo directory)
        {
            try
            {
                return directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
            }
            catch (IOException)
            {
                return directory.FullName;
            }
        }
    }
}
