using System.Diagnostics;
using System.Security.Cryptography;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>Enlaces, "Comprobar sumas" (como el modo Test de TeraCopy) y "Borrar" (con borrado seguro opcional).</summary>
public sealed partial class CopyEngine
{
    private void CreateLinks(IEnumerable<PlannedLink> links, CopyJobResult result)
    {
        foreach (var link in links)
        {
            try
            {
                FileSystemInfo existing = link.IsDirectory ? new DirectoryInfo(link.Destination) : new FileInfo(link.Destination);
                if (existing.Exists || existing.LinkTarget is not null)
                {
                    if (existing.LinkTarget == link.Target)
                    {
                        continue; // ya está (por ejemplo, al reanudar)
                    }

                    Log(LogLevel.Warning, $"Ya existe en el destino; el enlace no se recreó: {link.Destination}", link.Source);
                    continue;
                }

                string kind = Links.Create(link.Destination, link.Target, link.IsDirectory);
                Log(LogLevel.Info, $"Creado {kind}: {link.Destination} → {link.Target}", link.Source);
                if (_options.Move)
                {
                    if (link.IsDirectory)
                    {
                        Directory.Delete(link.Source); // solo quita el enlace, no lo que hay detrás
                    }
                    else
                    {
                        File.Delete(link.Source);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && !link.IsDirectory && CopyLinkedFileInstead(link))
            {
                // Sin permiso para enlaces de archivo (falta el modo de desarrollador): se copia su contenido.
                Log(LogLevel.Warning, $"No se pudo crear el enlace ({ex.Message}); se copió el archivo al que apunta.", link.Source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Failures.Add(new FileFailure(link.Source, ex.Message, FileErrors.Classify(ex)));
                Log(LogLevel.Error, $"No se pudo crear el enlace: {ex.Message}", link.Source);
            }
        }
    }

    /// <summary>Añade a la lista el archivo al que apunta el enlace (se copia en el bucle normal).</summary>
    private bool CopyLinkedFileInstead(PlannedLink link)
    {
        if (new FileInfo(link.Source).ResolveLinkTarget(returnFinalTarget: true) is not FileInfo { Exists: true } target)
        {
            return false;
        }

        Files.Add(link.Source, link.Destination, target.Length, target.LastWriteTimeUtc);
        return true;
    }

    // -------------------------------------------------------------------------------------------------------------
    // Comprobar sumas

    private CopyJobResult RunTest(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        var lists = ChecksumLists.Find(request.Sources);
        var expected = new Dictionary<int, ChecksumEntry>();
        var edited = new HashSet<int>();
        foreach (string list in lists)
        {
            foreach (var entry in ChecksumLists.Parse(list))
            {
                var info = new FileInfo(entry.Path);
                var item = Files.Add(entry.Path, entry.Path, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : default);
                expected[item.Id] = entry;
            }
        }

        if (lists.Count == 0)
        {
            // Sin archivos de sumas: se comprueban los archivos que guardan su hash dentro (copiados por MegaCopier
            // con "guardar el hash en el archivo"). Detecta la corrupción silenciosa del disco con el tiempo.
            string dummy = Path.Combine(Path.GetTempPath(), "MegaCopier-test-" + Guid.NewGuid().ToString("N"));
            foreach (var file in CopyPlanner.Build(request.Sources, dummy, _options.Filter, LinkHandling.Skip).Files)
            {
                if (ReadHashStream(file.Source) is { } stored)
                {
                    var item = Files.Add(file.Source, file.Source, file.Length, file.LastWriteUtc);
                    expected[item.Id] = new ChecksumEntry(file.Source, stored.Hash, stored.Algorithm);
                    if (stored.LastWriteUtc is { } stamp && Math.Abs((file.LastWriteUtc - stamp).TotalSeconds) > 2)
                    {
                        edited.Add(item.Id); // se modificó después: no es corrupción
                    }
                }
                else if (_options.SaveHashStream)
                {
                    // Renovar: lo que no tiene hash (nuevo o guardado de nuevo por otro programa) queda protegido.
                    var item = Files.Add(file.Source, file.Source, file.Length, file.LastWriteUtc);
                    expected[item.Id] = new ChecksumEntry(file.Source, "", _options.HashAlgorithm == FileHashAlgorithm.None ? FileHashAlgorithm.Sha256 : _options.HashAlgorithm);
                    edited.Add(item.Id);
                }
            }

            if (expected.Count == 0)
            {
                throw new CopyPlanException("No se encontró ningún archivo de sumas (.sha256, .md5, .sfv, .xxh3...) ni archivos con su hash guardado.");
            }
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        progress.SetPhase(CopyPhase.Verifying);
        _progress = progress;
        Log(LogLevel.Info, $"Comprobar {totalFiles} archivos de {lists.Count} listas de sumas ({FormatBytes(totalBytes)}).");

        byte[] buffer = new byte[CopyOptions.MiB];
        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            Files.Update(item, FileState.Verifying);
            progress.BeginFile(item.Source, item.Length);
            var entry = expected[item.Id];
            item.DestinationHash = entry.Hash;
            try
            {
                if (!File.Exists(item.Source))
                {
                    Fail(item, result, $"No existe: {item.Source}", FileErrorKind.NotFound);
                }
                else if (edited.Contains(item.Id))
                {
                    // Editado después de guardar su hash: se omite; con "guardar el hash en el archivo", se renueva.
                    bool unprotected = entry.Hash.Length == 0;
                    string message = unprotected ? "Sin hash guardado: no se comprueba" : "Modificado después de guardar su hash: no se comprueba";
                    if (_options.SaveHashStream)
                    {
                        string fresh = HashWithProgress(item.Source, entry.Algorithm, buffer, progress, cancellationToken);
                        WriteHashStream(item.Source, fresh, item.LastWriteUtc, entry.Algorithm);
                        message = unprotected ? "Sin hash guardado: ahora protegido" : "Modificado después de guardar su hash: hash renovado";
                    }

                    result.FilesSkipped++;
                    Files.Update(item, FileState.Skipped, message);
                }
                else
                {
                    string actual = HashWithProgress(item.Source, entry.Algorithm, buffer, progress, cancellationToken);
                    item.SourceHash = actual;
                    if (string.Equals(actual, entry.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        result.FilesCopied++;
                        result.BytesTransferred += item.Length;
                        Files.Update(item, FileState.Verified);
                    }
                    else
                    {
                        Fail(item, result, "El contenido no coincide con la suma guardada", FileErrorKind.ChecksumMismatch);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail(item, result, ex.Message, FileErrors.Classify(ex));
            }

            progress.EndFile();
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(
            result.Success ? LogLevel.Info : LogLevel.Warning,
            $"Comprobados en {result.Elapsed:hh\\:mm\\:ss}: {result.FilesCopied} correctos, {result.Failures.Count} con errores.");
        return result;
    }

    private string HashWithProgress(string path, FileHashAlgorithm algorithm, byte[] buffer, ProgressTracker progress, CancellationToken cancellationToken)
    {
        var hasher = FileHasher.Create(algorithm) ?? throw new CryptographicException($"Algoritmo no soportado: {algorithm}");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            hasher.Append(buffer.AsSpan(0, read));
            progress.AddTransferred(read);
        }

        return hasher.Finish();
    }

    // -------------------------------------------------------------------------------------------------------------
    // Borrar

    private CopyJobResult RunDelete(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();

        // El destino no se usa: solo hace falta uno que no esté dentro de los orígenes para planificar.
        string dummy = Path.Combine(Path.GetTempPath(), "MegaCopier-delete-" + Guid.NewGuid().ToString("N"));
        var plan = CopyPlanner.Build(request.Sources, dummy, _options.Filter, LinkHandling.CopyLink);
        var excluded = new HashSet<string>(request.Excluded, StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files.Where(f => !excluded.Contains(f.Source)))
        {
            Files.Add(file.Source, file.Source, file.Length, file.LastWriteUtc);
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();

        // El borrado normal no escribe: la barra avanza por archivos; el seguro sí sobrescribe todo el contenido.
        progress.Start(_options.SecureDelete ? totalBytes : 0, totalFiles);
        _progress = progress;
        Log(LogLevel.Info, $"{(_options.SecureDelete ? "Borrar de forma segura" : "Borrar")} {totalFiles} archivos ({FormatBytes(totalBytes)}).");

        byte[] zeros = new byte[CopyOptions.MiB];
        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.BeginFile(item.Source, _options.SecureDelete ? item.Length : 0);
            try
            {
                ClearReadOnly(item.Source);
                if (_options.SecureDelete)
                {
                    Wipe(item.Source, zeros, progress, cancellationToken);
                }
                else
                {
                    File.Delete(item.Source);
                }

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

        // Los enlaces se borran como enlaces: nunca se toca lo que hay detrás.
        foreach (var link in plan.Links.Where(l => !excluded.Contains(l.Source)))
        {
            try
            {
                if (link.IsDirectory)
                {
                    Directory.Delete(link.Source);
                }
                else
                {
                    File.Delete(link.Source);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Failures.Add(new FileFailure(link.Source, ex.Message, FileErrors.Classify(ex)));
            }
        }

        foreach (var directory in plan.Directories.OrderByDescending(d => d.Depth))
        {
            TryDeleteEmptyDirectory(directory.Source);
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(
            result.Success ? LogLevel.Info : LogLevel.Warning,
            $"Terminado en {result.Elapsed:hh\\:mm\\:ss}: {result.FilesCopied} borrados, {result.Failures.Count} con errores.");
        return result;
    }

    /// <summary>
    /// Borrado seguro: sobrescribe todo el contenido con ceros (escritura directa al disco), lo trunca, le cambia el
    /// nombre por uno aleatorio (para no dejar el original en la tabla de archivos) y lo borra.
    /// </summary>
    /// <remarks>En SSD el controlador puede conservar copias en otras celdas; es lo más que se puede hacer desde Windows.</remarks>
    private void Wipe(string path, byte[] zeros, ProgressTracker progress, CancellationToken cancellationToken)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
        {
            long remaining = stream.Length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(zeros.Length, remaining);
                stream.Write(zeros, 0, count);
                remaining -= count;
                progress.AddTransferred(count);
            }

            stream.Flush(flushToDisk: true);
            stream.SetLength(0);
        }

        string renamed = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N")[..12]);
        File.Move(path, renamed);
        File.Delete(renamed);
    }
}

/// <summary>Una línea de un archivo de sumas: la ruta del archivo y su hash esperado.</summary>
public sealed record ChecksumEntry(string Path, string Hash, FileHashAlgorithm Algorithm);

/// <summary>Lee archivos de sumas en los formatos habituales: sha256sum/md5sum, BSD ("SHA256 (x) = ..."), .sfv y xxhsum.</summary>
public static class ChecksumLists
{
    private static readonly Dictionary<string, FileHashAlgorithm> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md5"] = FileHashAlgorithm.Md5,
        [".sha1"] = FileHashAlgorithm.Sha1,
        [".sha256"] = FileHashAlgorithm.Sha256,
        [".sha512"] = FileHashAlgorithm.Sha512,
        [".sha3-256"] = FileHashAlgorithm.Sha3_256,
        [".sha3-512"] = FileHashAlgorithm.Sha3_512,
        [".sfv"] = FileHashAlgorithm.Crc32,
        [".xxh3"] = FileHashAlgorithm.XxHash3,
        [".xxh64"] = FileHashAlgorithm.XxHash64,
        [".xxh128"] = FileHashAlgorithm.XxHash128,
    };

    public static bool IsChecksumFile(string path) => ByExtension.ContainsKey(Path.GetExtension(path));

    /// <summary>Los archivos de sumas indicados, y los que haya dentro de las carpetas indicadas.</summary>
    public static IReadOnlyList<string> Find(IEnumerable<string> sources)
    {
        var found = new List<string>();
        foreach (string source in sources)
        {
            if (File.Exists(source))
            {
                found.Add(Path.GetFullPath(source));
            }
            else if (Directory.Exists(source))
            {
                found.AddRange(Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(IsChecksumFile));
            }
        }

        return found;
    }

    public static IEnumerable<ChecksumEntry> Parse(string listPath)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(listPath))!;
        ByExtension.TryGetValue(Path.GetExtension(listPath), out var byExtension);
        bool sfv = byExtension == FileHashAlgorithm.Crc32;

        foreach (string raw in File.ReadLines(listPath))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            string path;
            string hash;
            var algorithm = byExtension;
            int open = line.IndexOf(" (", StringComparison.Ordinal);
            int close = line.LastIndexOf(") = ", StringComparison.Ordinal);
            if (open > 0 && close > open)
            {
                // BSD: "SHA256 (archivo) = hash"
                algorithm = line[..open].ToUpperInvariant() switch
                {
                    "MD5" => FileHashAlgorithm.Md5,
                    "SHA1" => FileHashAlgorithm.Sha1,
                    "SHA256" => FileHashAlgorithm.Sha256,
                    "SHA512" => FileHashAlgorithm.Sha512,
                    "SHA3-256" => FileHashAlgorithm.Sha3_256,
                    "SHA3-512" => FileHashAlgorithm.Sha3_512,
                    "XXH3" or "XXH3_64" => FileHashAlgorithm.XxHash3,
                    "XXH64" => FileHashAlgorithm.XxHash64,
                    "XXH128" => FileHashAlgorithm.XxHash128,
                    _ => algorithm,
                };
                path = line[(open + 2)..close];
                hash = line[(close + 4)..].Trim();
            }
            else if (sfv)
            {
                // .sfv: "archivo HASH"
                int space = line.LastIndexOf(' ');
                if (space <= 0)
                {
                    continue;
                }

                path = line[..space].Trim();
                hash = line[(space + 1)..];
            }
            else
            {
                // sha256sum / xxhsum: "hash *archivo" o "hash  archivo"
                int space = line.IndexOf(' ');
                if (space <= 0)
                {
                    continue;
                }

                hash = line[..space];
                path = line[(space + 1)..].TrimStart(' ', '*');
            }

            hash = hash.StartsWith("XXH3_", StringComparison.OrdinalIgnoreCase) ? hash[5..] : hash;
            if (algorithm == FileHashAlgorithm.None)
            {
                // Extensión desconocida: se deduce por la longitud del hash.
                algorithm = hash.Length switch
                {
                    8 => FileHashAlgorithm.Crc32,
                    32 => FileHashAlgorithm.Md5,
                    40 => FileHashAlgorithm.Sha1,
                    64 => FileHashAlgorithm.Sha256,
                    128 => FileHashAlgorithm.Sha512,
                    _ => FileHashAlgorithm.None,
                };
            }

            if (algorithm == FileHashAlgorithm.None || path.Length == 0)
            {
                continue;
            }

            path = path.Replace('/', Path.DirectorySeparatorChar);
            yield return new ChecksumEntry(Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(folder, path)), hash.ToLowerInvariant(), algorithm);
        }
    }
}
