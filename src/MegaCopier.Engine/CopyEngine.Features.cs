using System.Diagnostics;
using System.Text;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>
/// Simular, sincronizar (espejo), versiones, enlaces duros, hash guardado en el archivo, esperar a que vuelva la unidad
/// o haya espacio, y crear archivos de sumas sin copiar.
/// </summary>
public sealed partial class CopyEngine
{
    /// <summary>Nombre del flujo alternativo donde se guarda el hash del archivo copiado ("sha256:...").</summary>
    public const string HashStreamName = "MegaCopier.hash";

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Volume, ulong Index), string> _hardLinks = new();

    // -------------------------------------------------------------------------------------------------------------
    // Simular

    /// <summary>Calcula qué se haría con el archivo, sin tocar nada.</summary>
    private void SimulateFile(TransferItem item, CopyJobResult result)
    {
        var file = new PlannedFile(item.Source, item.Destination, item.Length, item.LastWriteUtc);
        var existing = new FileInfo(file.Destination);
        PlannedAction action;
        if (string.Equals(file.Source, file.Destination, StringComparison.OrdinalIgnoreCase))
        {
            action = PlannedAction.SkipIdentical;
        }
        else if (!existing.Exists)
        {
            action = _options.Mode == CopyMode.UpdateExisting ? PlannedAction.SkipNotInDestination : PlannedAction.Copy;
        }
        else if (!_options.ForceRecopy && IsIdentical(existing, file))
        {
            action = PlannedAction.SkipIdentical;
        }
        else
        {
            action = _options.Conflict switch
            {
                ConflictPolicy.Overwrite => PlannedAction.Replace,
                ConflictPolicy.Skip => PlannedAction.SkipExists,
                ConflictPolicy.OverwriteIfNewer => file.LastWriteUtc > existing.LastWriteTimeUtc + _options.TimeTolerance ? PlannedAction.ReplaceNewer : PlannedAction.SkipOlder,
                ConflictPolicy.OverwriteIfDifferentSize => file.Length != existing.Length ? PlannedAction.ReplaceSize : PlannedAction.SkipSameSize,
                ConflictPolicy.KeepBoth => PlannedAction.KeepBoth,
                ConflictPolicy.RenameExisting => PlannedAction.RenameExisting,
                ConflictPolicy.Ask => PlannedAction.Ask,
                _ => PlannedAction.Fail,
            };
        }

        bool copies = action is PlannedAction.Copy or PlannedAction.Replace or PlannedAction.ReplaceNewer or PlannedAction.ReplaceSize
            or PlannedAction.KeepBoth or PlannedAction.RenameExisting or PlannedAction.Ask;
        if (copies)
        {
            result.FilesCopied++;
            result.BytesTransferred += item.Length;
        }
        else
        {
            result.FilesSkipped++;
        }

        item.Action = action;
        Files.Update(item, FileState.Planned, ActionText(action));
    }

    /// <summary>Texto (en el registro y los informes); la interfaz lo traduce a partir de <see cref="TransferItem.Action"/>.</summary>
    private string ActionText(PlannedAction action) => action switch
    {
        PlannedAction.Copy => _options.Move ? "Se movería" : "Se copiaría",
        PlannedAction.Replace => "Se reemplazaría el existente",
        PlannedAction.ReplaceNewer => "Se reemplazaría: el origen es más reciente",
        PlannedAction.ReplaceSize => "Se reemplazaría: cambió el tamaño",
        PlannedAction.KeepBoth => "Se copiaría con otro nombre (se conservan ambos)",
        PlannedAction.RenameExisting => "Se renombraría el existente y se copiaría",
        PlannedAction.Ask => "Se preguntaría qué hacer",
        PlannedAction.SkipIdentical => "Se omitiría: idéntico",
        PlannedAction.SkipExists => "Se omitiría: ya existe",
        PlannedAction.SkipOlder => "Se omitiría: el destino es igual o más reciente",
        PlannedAction.SkipSameSize => "Se omitiría: mismo tamaño",
        PlannedAction.SkipNotInDestination => "Se omitiría: no existe en el destino",
        PlannedAction.Delete => _options.DryRun ? "Se borraría del destino: no está en el origen" : "Borrado del destino: no estaba en el origen",
        PlannedAction.Fail => "Se marcaría como error",
        PlannedAction.SkipSamePath => "Se omitiría: el origen y el destino son el mismo",
        _ => "",
    };

    /// <summary>Por qué se omitió un archivo (registro e informes); la interfaz lo traduce con <see cref="TransferItem.Action"/>.</summary>
    private static string SkipText(PlannedAction action) => action switch
    {
        PlannedAction.SkipIdentical => "Idéntico al existente",
        PlannedAction.SkipOlder => "El destino es igual o más reciente",
        PlannedAction.SkipSameSize => "Mismo tamaño que el existente",
        PlannedAction.SkipNotInDestination => "No existe en el destino (solo se actualizan los existentes)",
        PlannedAction.SkipSamePath => "El origen y el destino son el mismo archivo",
        _ => "Ya existía: omitido",
    };

    // -------------------------------------------------------------------------------------------------------------
    // Sincronizar (espejo)

    /// <summary>
    /// Borra del destino lo que ya no está en el origen, dentro de cada carpeta copiada (nunca fuera de ellas). Con
    /// filtros, solo se tocan los archivos que los filtros aceptarían: lo que excluyen se deja en paz.
    /// </summary>
    private void MirrorExtras(CopyJobRequest request, CopyPlan plan, CopyJobResult result, CancellationToken cancellationToken)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            expected.Add(file.Destination);
        }

        foreach (var directory in plan.Directories)
        {
            expected.Add(directory.Destination);
        }

        foreach (var link in plan.Links)
        {
            expected.Add(link.Destination);
        }

        string? versions = VersionsRoot();
        var filter = _options.Filter is { IsEmpty: false } f ? f : null;
        foreach (string raw in request.Sources)
        {
            string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            if (!Directory.Exists(source))
            {
                continue;
            }

            string targetRoot = Path.Combine(_destinationRoot, Path.GetFileName(source));
            if (Directory.Exists(targetRoot))
            {
                MirrorDirectory(new DirectoryInfo(targetRoot), targetRoot, expected, versions, filter, result, cancellationToken);
            }
        }
    }

    private void MirrorDirectory(DirectoryInfo directory, string targetRoot, HashSet<string> expected, string? versions, FileFilter? filter,
        CopyJobResult result, CancellationToken cancellationToken)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (versions is not null && entry.FullName.StartsWith(versions, StringComparison.OrdinalIgnoreCase))
            {
                continue; // la carpeta de versiones nunca se sincroniza
            }

            if (entry is FileInfo file && CopyPlanner.IsTransferArtifact(file.Name))
            {
                continue;
            }

            string relative = Path.GetRelativePath(targetRoot, entry.FullName);
            bool isLink = (entry.Attributes & FileAttributes.ReparsePoint) != 0 && entry.LinkTarget is not null;
            if (expected.Contains(entry.FullName))
            {
                if (entry is DirectoryInfo sub && !isLink)
                {
                    MirrorDirectory(sub, targetRoot, expected, versions, filter, result, cancellationToken);
                }

                continue;
            }

            if (filter is not null && entry is FileInfo candidate && !filter.AcceptsFile(relative, candidate))
            {
                continue; // fuera del alcance de los filtros: se respeta
            }

            if (entry is DirectoryInfo extraDirectory && !isLink)
            {
                // Una carpeta que sobra: primero su contenido (para mostrarlo y versionarlo), luego la carpeta.
                MirrorDirectory(extraDirectory, targetRoot, expected, versions, filter, result, cancellationToken);
                if (!_options.DryRun)
                {
                    TryDeleteEmptyDirectory(extraDirectory.FullName);
                }

                continue;
            }

            long length = entry is FileInfo info ? info.Length : 0;
            var item = Files.Add(entry.FullName, entry.FullName, length, entry.LastWriteTimeUtc);
            item.Action = PlannedAction.Delete;
            result.FilesDeleted++;
            if (_options.DryRun)
            {
                Files.Update(item, FileState.Planned, ActionText(PlannedAction.Delete));
                continue;
            }

            try
            {
                if (isLink)
                {
                    if (entry is DirectoryInfo)
                    {
                        Directory.Delete(entry.FullName);
                    }
                    else
                    {
                        File.Delete(entry.FullName);
                    }
                }
                else
                {
                    ClearReadOnly(entry.FullName);
                    if (versions is not null)
                    {
                        KeepVersion(entry.FullName);
                    }
                    else
                    {
                        File.Delete(entry.FullName);
                    }
                }

                Files.Update(item, FileState.Deleted, ActionText(PlannedAction.Delete));
                Log(LogLevel.Info, "Borrado del destino (no está en el origen).", entry.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.FilesDeleted--;
                Fail(item, result, ex.Message, FileErrors.Classify(ex));
            }
        }
    }

    /// <summary>"Si la carpeta ya existe: omitirla". Solo en la primera ejecución: al reanudar, la carpeta es nuestra.</summary>
    private CopyPlan SkipExistingFolders(CopyJobRequest request, CopyPlan plan)
    {
        var skipped = new List<string>();
        foreach (string raw in request.Sources)
        {
            string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            if (Directory.Exists(source) && Directory.Exists(Path.Combine(_destinationRoot, Path.GetFileName(source))))
            {
                skipped.Add(source);
                Log(LogLevel.Warning, "La carpeta ya existe en el destino: omitida.", source);
            }
        }

        if (skipped.Count == 0)
        {
            return plan;
        }

        bool Inside(string path) => skipped.Any(s =>
            string.Equals(path, s, StringComparison.OrdinalIgnoreCase) || path.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        return new CopyPlan(
            [.. plan.Files.Where(f => !Inside(f.Source))],
            [.. plan.Directories.Where(d => !Inside(d.Source))],
            [.. plan.Links.Where(l => !Inside(l.Source))],
            plan.Warnings);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Versiones

    private string? VersionsRoot()
    {
        if (string.IsNullOrWhiteSpace(_options.VersionsFolder))
        {
            return null;
        }

        return Path.TrimEndingDirectorySeparator(Path.IsPathFullyQualified(_options.VersionsFolder)
            ? _options.VersionsFolder
            : Path.Combine(_destinationRoot, _options.VersionsFolder));
    }

    /// <summary>Aparta un archivo del destino a la carpeta de versiones: "nombre 2026-10-16 153000.ext".</summary>
    private void KeepVersion(string path)
    {
        if (VersionsRoot() is not { } root)
        {
            return;
        }

        string relative = path.StartsWith(_destinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(_destinationRoot, path)
            : Path.GetFileName(path);
        string folder = Path.Combine(root, Path.GetDirectoryName(relative) ?? "");
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)} {stamp}{Path.GetExtension(path)}");
        for (int n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)} {stamp} ({n}){Path.GetExtension(path)}");
        }

        ClearReadOnly(path);
        File.Move(path, target);
        Log(LogLevel.Info, $"Versión anterior guardada en {target}", path);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Enlaces duros

    private bool TryHardLink(TransferItem item, string target, CopyJobResult result)
    {
        if (Links.GetLinkInfo(item.Source) is not { Count: > 1 } info
            || !_hardLinks.TryGetValue((info.Volume, info.Index), out string? first)
            || !File.Exists(first))
        {
            return false;
        }

        try
        {
            if (File.Exists(target))
            {
                ClearReadOnly(target);
                File.Delete(target);
            }

            Links.CreateHardLink(target, first);
            result.FilesCopied++;
            Files.Update(item, FileState.Copied, $"Enlace duro a {Path.GetFileName(first)}");
            Log(LogLevel.Info, $"Enlace duro creado (mismo archivo que {first}).", item.Source);
            if (_options.Move)
            {
                TryDeleteSource(item, result);
            }

            return true;
        }
        catch (IOException ex)
        {
            Log(LogLevel.Warning, $"No se pudo crear el enlace duro ({ex.Message}); se copia el archivo.", item.Source);
            return false;
        }
    }

    private void RememberHardLink(TransferItem item, string target)
    {
        if (_options.PreserveHardLinks && Links.GetLinkInfo(item.Source) is { Count: > 1 } info)
        {
            _hardLinks.TryAdd((info.Volume, info.Index), target);
        }
    }

    // -------------------------------------------------------------------------------------------------------------
    // Hash guardado en el propio archivo

    /// <summary>Guarda "algoritmo:hash" en el flujo :MegaCopier.hash sin cambiar la fecha del archivo.</summary>
    private void WriteHashStream(string path, string hash, DateTime lastWriteUtc, FileHashAlgorithm? algorithmOverride = null)
    {
        var algorithm = algorithmOverride ?? _options.HashAlgorithm;
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            // Con la fecha de modificación: así se distingue un archivo que se editó (fecha nueva) de uno que se dañó (misma fecha).
            File.WriteAllText($"{path}:{HashStreamName}", $"{FileHasher.Extension(algorithm)}:{hash}:{lastWriteUtc.Ticks}", new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(path, lastWriteUtc);
            File.SetAttributes(path, attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log(LogLevel.Warning, $"No se pudo guardar el hash en el archivo (¿el destino no es NTFS?): {ex.Message}", path);
        }
    }

    /// <summary>El hash guardado en un archivo por MegaCopier, o null.</summary>
    public static (FileHashAlgorithm Algorithm, string Hash, DateTime? LastWriteUtc)? ReadHashStream(string path)
    {
        try
        {
            string text = File.ReadAllText($"{path}:{HashStreamName}").Trim();
            int colon = text.IndexOf(':');
            if (colon <= 0)
            {
                return null;
            }

            string extension = text[..colon];
            foreach (var algorithm in Enum.GetValues<FileHashAlgorithm>())
            {
                if (algorithm != FileHashAlgorithm.None && FileHasher.Extension(algorithm) == extension)
                {
                    // "sha256:hash:fecha" (la fecha falta en los guardados por versiones anteriores).
                    string[] rest = text[(colon + 1)..].Split(':');
                    DateTime? stamp = rest.Length > 1 && long.TryParse(rest[1], out long ticks) ? new DateTime(ticks, DateTimeKind.Utc) : null;
                    return (algorithm, rest[0].ToLowerInvariant(), stamp);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }

        return null;
    }

    // -------------------------------------------------------------------------------------------------------------
    // Esperar a que vuelva la unidad o haya espacio

    /// <returns>true si el problema se resolvió y hay que reintentar (sin gastar un reintento).</returns>
    private bool WaitForRecovery(FileErrorKind kind, TransferItem item, string target, ProgressTracker progress, CancellationToken cancellationToken)
    {
        if (_options.WaitForDevice && kind is FileErrorKind.NetworkLost or FileErrorKind.NotFound && !Reachable(item.Source))
        {
            progress.SetPhase(CopyPhase.WaitingForDevice);
            Log(LogLevel.Warning, "Se perdió la red o la unidad: la copia espera a que vuelva y seguirá sola.", item.Source);
            var deadline = DateTime.UtcNow + _options.DeviceWaitTimeout;
            while (!Reachable(item.Source))
            {
                if (DateTime.UtcNow > deadline)
                {
                    progress.SetPhase(CopyPhase.Copying);
                    return false;
                }

                cancellationToken.WaitHandle.WaitOne(_options.WaitPollInterval);
                cancellationToken.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(cancellationToken);
            }

            Log(LogLevel.Info, "La unidad volvió: se continúa desde donde se quedó.", item.Source);
            progress.SetPhase(CopyPhase.Copying);
            return true;
        }

        if (_options.WaitForSpace && kind == FileErrorKind.DiskFull)
        {
            progress.SetPhase(CopyPhase.WaitingForSpace);
            Log(LogLevel.Warning, "El destino está lleno: libera espacio y la copia seguirá sola.", target);
            long needed = item.Length + (16L << 20);
            while ((Win32.GetFreeBytes(target) ?? long.MaxValue) < needed)
            {
                cancellationToken.WaitHandle.WaitOne(_options.WaitPollInterval);
                cancellationToken.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(cancellationToken);
            }

            Log(LogLevel.Info, "Ya hay espacio: se continúa.", target);
            progress.SetPhase(CopyPhase.Copying);
            return true;
        }

        return false;

        // La carpeta del origen y la raíz del destino responden.
        bool Reachable(string source) =>
            Directory.Exists(Path.GetDirectoryName(source)) && Directory.Exists(_destinationRoot);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Crear archivo de sumas sin copiar

    private CopyJobResult RunHash(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        string dummy = Path.Combine(Path.GetTempPath(), "MegaCopier-hash-" + Guid.NewGuid().ToString("N"));
        var plan = CopyPlanner.Build(request.Sources, dummy, _options.Filter, LinkHandling.Skip);
        var algorithm = _options.HashAlgorithm == FileHashAlgorithm.None ? FileHashAlgorithm.Sha256 : _options.HashAlgorithm;
        foreach (var file in plan.Files.Where(f => !ChecksumLists.IsChecksumFile(f.Source)))
        {
            Files.Add(file.Source, file.Source, file.Length, file.LastWriteUtc);
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        _progress = progress;

        // El archivo de sumas va en la carpeta elegida; si solo se eligió una carpeta, dentro de ella y con su nombre.
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        string single = request.Sources.Count == 1 && Directory.Exists(request.Sources[0]) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Sources[0])) : "";
        if (single.Length > 0)
        {
            folder = single;
        }

        string name = single.Length > 0 ? Path.GetFileName(single) : $"MegaCopier_{DateTime.Now:yyyyMMdd_HHmmss}";
        string listPath = Path.Combine(folder, $"{name}.{FileHasher.Extension(algorithm)}");
        Log(LogLevel.Info, $"Calcular {FileHasher.Extension(algorithm).ToUpperInvariant()} de {totalFiles} archivos ({FormatBytes(totalBytes)}).");

        byte[] buffer = new byte[CopyOptions.MiB];
        using var readAhead = ReadAhead.TryCreate(Files, _options);
        var lines = new StringBuilder();
        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.BeginFile(item.Source, item.Length);
            try
            {
                string hash = HashWithProgress(item, algorithm, buffer, progress, readAhead, cancellationToken);
                item.SourceHash = hash;
                if (_options.SaveHashStream)
                {
                    // Proteger la carpeta: el hash va dentro de cada archivo (el guardián de integridad lo comprueba).
                    WriteHashStream(item.Source, hash, item.LastWriteUtc, algorithm);
                }

                string relative = Path.GetRelativePath(folder, item.Source);
                lines.Append(algorithm == FileHashAlgorithm.Crc32 ? $"{relative} {hash.ToUpperInvariant()}\n" : $"{hash} *{relative}\n");
                result.FilesCopied++;
                result.BytesTransferred += item.Length;
                Files.Update(item, FileState.Copied);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail(item, result, ex.Message, FileErrors.Classify(ex));
            }

            progress.EndFile();
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        if (_options.SaveHashStream)
        {
            Log(LogLevel.Info, $"Hash guardado dentro de {result.FilesCopied} archivos.");
            return result;
        }

        File.WriteAllText(listPath, lines.ToString(), new UTF8Encoding(false));
        ChecksumFile = listPath;
        Log(LogLevel.Info, $"Archivo de sumas guardado: {listPath}");
        return result;
    }
}
