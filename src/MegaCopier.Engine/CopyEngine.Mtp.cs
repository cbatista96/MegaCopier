using System.Diagnostics;
using MegaCopier.Engine.IO;
using MegaCopier.Engine.Remote;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>Abre un móvil o cámara por su id (las pruebas ponen uno de mentira).</summary>
    internal Func<string, IRemoteStore> RemoteOpener { get; set; } = RemoteStores.Open;

    private enum MtpAction
    {
        Copy,
        Replace,
        Skip,
        KeepBoth,
        RenameExisting,
        Fail,
    }

    private static bool IsRemoteJob(CopyJobRequest request) =>
        RemotePath.IsRemote(request.Destination) || request.Sources.Any(RemotePath.IsRemote);

    /// <summary>
    /// Del móvil al PC: recorre las carpetas del dispositivo, copia cada archivo a un .part (con su hash), lo renombra
    /// y le pone la fecha del móvil. Al reanudar, lo que ya llegó entero (mismo tamaño y fecha) se salta.
    /// Al mover, cada archivo se borra del móvil solo después de copiarlo (y verificarlo, si se pidió).
    /// </summary>
    private CopyJobResult RunMtpImport(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        _destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        var devices = new Dictionary<string, IRemoteStore>(StringComparer.OrdinalIgnoreCase);
        var objects = new Dictionary<int, RemoteItem>();
        var topFolders = new List<(IRemoteStore Device, RemoteItem Folder, List<TransferItem> Items)>();
        try
        {
            foreach (string source in request.Sources)
            {
                if (!RemotePath.TryParse(source, out string deviceId, out _, out string inner))
                {
                    throw new CopyPlanException(Texts.F("E_SourceMissing", source));
                }

                if (!devices.TryGetValue(deviceId, out var device))
                {
                    devices[deviceId] = device = RemoteOpener(deviceId);
                }

                var found = RemoteStores.Resolve(device, inner)
                    ?? (_options.Move && request.Resumed ? null : throw new CopyPlanException(Texts.F("E_SourceMissing", RemotePath.Display(source))));
                if (found is null)
                {
                    continue; // al reanudar un movimiento: ya se movió entero
                }

                string target = Path.Combine(_destinationRoot, SafeName(found.Name));
                if (found.IsFolder)
                {
                    var items = new List<TransferItem>();
                    PlanFolder(device, found, source, target, found.Name, objects, items);
                    topFolders.Add((device, found, items));
                }
                else if (_options.Filter.AcceptsFile(found.Name, found.Size, found.ModifiedUtc))
                {
                    var item = Files.Add(source, target, found.Size, found.ModifiedUtc ?? DateTime.MinValue);
                    objects[item.Id] = found;
                }
            }

            var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
            var (totalBytes, totalFiles) = Files.Totals();
            progress.Start(totalBytes, totalFiles);
            _progress = progress;
            Log(LogLevel.Info, $"Copiar {totalFiles} archivos ({FormatBytes(totalBytes)}) del dispositivo a {request.Destination}.");
            var throttle = new Throttle(() => _options.MaxBytesPerSecond);

            while (Files.TakeNext() is { } item)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(cancellationToken);
                if (!objects.TryGetValue(item.Id, out var obj) || !RemotePath.TryParse(item.Source, out string deviceId, out _, out _))
                {
                    continue;
                }

                var device = devices[deviceId];
                progress.BeginFile(item.Destination, item.Length);
                try
                {
                    ImportOne(device, obj, item, result, progress, throttle, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    IoHelpers.DeleteIfExists(item.Destination + FileTransfer.PartialSuffix);
                    Fail(item, result, ex.Message, FileErrors.Classify(ex));
                }

                progress.EndFile();
            }

            // Mover carpetas: si todo lo de dentro llegó bien, la carpeta entera se borra del móvil (como el Explorador).
            if (_options.Move && !_options.DryRun)
            {
                foreach (var (device, folder, items) in topFolders)
                {
                    if (items.All(i => i.State is FileState.Copied or FileState.Verified || (i.State == FileState.Skipped && i.Message == Texts.Get("E_SkipIdentical"))))
                    {
                        TryMtp(() => device.Delete(folder.Id, recursive: true));
                    }
                }
            }

            progress.Finish();
        }
        finally
        {
            foreach (var device in devices.Values)
            {
                device.Dispose();
            }
        }

        result.Elapsed = stopwatch.Elapsed;
        Log(LogLevel.Info, Texts.F("E_DoneCopy", result.Elapsed.ToString(@"hh\:mm\:ss"), result.FilesCopied, result.FilesSkipped, result.Failures.Count));
        return result;
    }

    private void PlanFolder(IRemoteStore device, RemoteItem folder, string sourcePath, string targetPath, string relative,
        Dictionary<int, RemoteItem> objects, List<TransferItem> items)
    {
        if (!_options.Filter.AcceptsDirectory(relative, FileAttributes.Directory))
        {
            return;
        }

        if (!_options.DryRun)
        {
            Directory.CreateDirectory(targetPath);
        }

        foreach (var child in device.Children(folder.Id).OrderBy(c => c.IsFolder).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            string childSource = RemotePath.Combine(sourcePath, child.Name);
            string childTarget = Path.Combine(targetPath, SafeName(child.Name));
            string childRelative = relative + Path.DirectorySeparatorChar + child.Name;
            if (child.IsFolder)
            {
                PlanFolder(device, child, childSource, childTarget, childRelative, objects, items);
            }
            else if (_options.Filter.AcceptsFile(childRelative, child.Size, child.ModifiedUtc))
            {
                var item = Files.Add(childSource, childTarget, child.Size, child.ModifiedUtc ?? DateTime.MinValue);
                objects[item.Id] = child;
                items.Add(item);
            }
        }
    }

    private void ImportOne(IRemoteStore device, RemoteItem obj, TransferItem item, CopyJobResult result, ProgressTracker progress, Throttle throttle, CancellationToken cancellationToken)
    {
        if (File.Exists(item.Destination))
        {
            var existing = new FileInfo(item.Destination);
            var action = DecideMtpConflict(existing.Length, existing.LastWriteTimeUtc, obj.Size, obj.ModifiedUtc, sizeOnly: false, out string? reason);
            switch (action)
            {
                case MtpAction.Skip:
                    result.FilesSkipped++;
                    Files.Update(item, FileState.Skipped, reason);
                    if (_options.Move && !_options.DryRun && reason == Texts.Get("E_SkipIdentical"))
                    {
                        TryMtp(() => device.Delete(obj.Id, recursive: false)); // ya está en el PC: moverlo es quitarlo del móvil
                    }

                    return;
                case MtpAction.Fail:
                    Fail(item, result, Texts.F("E_DestinationExists", item.Destination));
                    return;
                case MtpAction.KeepBoth:
                    item.Destination = FreeName(item.Destination, candidate => !File.Exists(candidate) && !Directory.Exists(candidate));
                    break;
                case MtpAction.RenameExisting when !_options.DryRun:
                    string renamed = FreeName(item.Destination, candidate => !File.Exists(candidate) && !Directory.Exists(candidate));
                    File.Move(item.Destination, renamed);
                    Log(LogLevel.Info, Texts.F("E_ExistingRenamed", Path.GetFileName(renamed)), item.Destination);
                    break;
            }
        }

        if (_options.DryRun)
        {
            result.FilesCopied++;
            Files.Update(item, FileState.Planned, Texts.Get("E_WouldCopy"));
            return;
        }

        string partial = item.Destination + FileTransfer.PartialSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
        var hasher = FileHasher.Create(_options.HashAlgorithm == FileHashAlgorithm.None ? FileHashAlgorithm.XxHash3 : _options.HashAlgorithm)!;
        long copied = 0;
        using (var input = device.OpenRead(obj.Id))
        using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            byte[] buffer = new byte[1 << 20];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(cancellationToken);
                if (_options.MaxBytesPerSecond > 0)
                {
                    throttle.WaitAsync(read, cancellationToken).GetAwaiter().GetResult();
                }

                output.Write(buffer, 0, read);
                hasher.Append(buffer.AsSpan(0, read));
                copied += read;
                progress.AddTransferred(read);
            }

            output.Flush(flushToDisk: true);
        }

        if (obj.Size > 0 && copied != obj.Size)
        {
            IoHelpers.DeleteIfExists(partial);
            Fail(item, result, Texts.Get("E_SourceSizeChanged"), FileErrorKind.Other);
            return;
        }

        if (obj.ModifiedUtc is { } modified)
        {
            File.SetLastWriteTimeUtc(partial, modified);
        }

        File.Move(partial, item.Destination, overwrite: true);
        item.SourceHash = hasher.Finish();

        if (_options.Verify && !VerifyLocal(item))
        {
            result.Failures.Add(new FileFailure(item.Source, Texts.Get("E_HashMismatch"), FileErrorKind.ChecksumMismatch));
            return;
        }

        // "Volver a leer el original": un cable o un móvil que entregan mal un byte no se notan comparando solo el PC.
        if (_options.VerifySource && !string.Equals(HashDeviceObject(device, obj.Id), item.SourceHash, StringComparison.OrdinalIgnoreCase))
        {
            IoHelpers.DeleteIfExists(item.Destination);
            Fail(item, result, Texts.Get("E_SourceChanged"), FileErrorKind.ChecksumMismatch);
            return;
        }

        result.FilesCopied++;
        result.BytesTransferred += copied;
        Files.Update(item, _options.Verify ? FileState.Verified : FileState.Copied);

        if (_options.Move)
        {
            TryMtp(() => device.Delete(obj.Id, recursive: false));
        }
    }

    private string HashDeviceObject(IRemoteStore device, string objectId)
    {
        var hasher = FileHasher.Create(_options.HashAlgorithm == FileHashAlgorithm.None ? FileHashAlgorithm.XxHash3 : _options.HashAlgorithm)!;
        using (var stream = device.OpenRead(objectId))
        {
            byte[] buffer = new byte[1 << 20];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.Append(buffer.AsSpan(0, read));
            }
        }

        return hasher.Finish();
    }

    /// <summary>Relee sin caché lo escrito en el PC y lo compara con el hash calculado al leer del dispositivo.</summary>
    private bool VerifyLocal(TransferItem item)
    {
        var hasher = FileHasher.Create(_options.HashAlgorithm == FileHashAlgorithm.None ? FileHashAlgorithm.XxHash3 : _options.HashAlgorithm)!;
        using (var stream = new FileStream(item.Destination, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan | (FileOptions)0x20000000 /* sin caché */))
        {
            byte[] buffer = GC.AllocateUninitializedArray<byte>(1 << 20, pinned: true);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.Append(buffer.AsSpan(0, read));
            }
        }

        item.DestinationHash = hasher.Finish();
        if (string.Equals(item.DestinationHash, item.SourceHash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        IoHelpers.DeleteIfExists(item.Destination);
        Files.Update(item, FileState.Failed, Texts.Get("E_HashMismatch"), FileErrorKind.ChecksumMismatch);
        Log(LogLevel.Error, Texts.Get("E_HashMismatch"), item.Source);
        return false;
    }

    /// <summary>
    /// Del PC al móvil: crea las carpetas que falten y sube cada archivo. Un archivo del mismo nombre y tamaño ya en el
    /// móvil se da por copiado (las fechas de los móviles no son fiables). Al mover, el original se borra del PC solo
    /// después de subirlo (y verificarlo, si se pidió, leyéndolo de vuelta del móvil).
    /// </summary>
    private CopyJobResult RunMtpExport(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        if (!RemotePath.TryParse(request.Destination, out string deviceId, out _, out string inner))
        {
            throw new CopyPlanException(Texts.F("E_SourceMissing", request.Destination));
        }

        using var device = RemoteOpener(deviceId);
        var root = RemoteStores.Resolve(device, inner);
        if (root is not { IsFolder: true })
        {
            throw new CopyPlanException(Texts.F("E_SourceMissing", RemotePath.Display(request.Destination)));
        }

        // El planificador de siempre, con una raíz de mentira: de ella sale la ruta relativa de cada archivo.
        string fakeRoot = Path.Combine(Path.GetTempPath(), "megacopier-mtp-" + Guid.NewGuid().ToString("N"));
        var plan = CopyPlanner.Build(request.Sources, fakeRoot, _options.Filter, _options.Links);
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [""] = root.Id };
        var listing = new Dictionary<string, List<RemoteItem>>(StringComparer.OrdinalIgnoreCase);

        string Relative(string fake) => Path.GetRelativePath(fakeRoot, fake);
        string Folder(string relativeDirectory)
        {
            if (folders.TryGetValue(relativeDirectory, out string? id))
            {
                return id;
            }

            string parent = Folder(Path.GetDirectoryName(relativeDirectory) ?? "");
            string name = Path.GetFileName(relativeDirectory);
            var existing = Listing(parent).FirstOrDefault(o => o.IsFolder && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            id = existing?.Id ?? (_options.DryRun ? "dry:" + relativeDirectory : device.CreateFolder(parent, name));
            if (existing is null && !_options.DryRun)
            {
                Listing(parent).Add(new RemoteItem(id, name, true, 0, null));
            }

            folders[relativeDirectory] = id;
            return id;
        }

        List<RemoteItem> Listing(string folderId)
        {
            if (!listing.TryGetValue(folderId, out var list))
            {
                listing[folderId] = list = folderId.StartsWith("dry:", StringComparison.Ordinal) ? [] : [.. device.Children(folderId)];
            }

            return list;
        }

        foreach (var directory in plan.Directories)
        {
            Folder(Relative(directory.Destination));
        }

        foreach (var file in plan.Files)
        {
            Files.Add(file.Source, RemotePath.Combine(request.Destination, Relative(file.Destination).Replace('\\', '/')), file.Length, file.LastWriteUtc);
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        _progress = progress;
        Log(LogLevel.Info, $"Copiar {totalFiles} archivos ({FormatBytes(totalBytes)}) a {RemotePath.Display(request.Destination)}.");

        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.BeginFile(item.Source, item.Length);
            try
            {
                RemotePath.TryParse(item.Destination, out _, out _, out string innerTarget);
                string relative = innerTarget[inner.Length..].Trim('/');
                string parentId = Folder(Path.GetDirectoryName(relative.Replace('/', '\\')) ?? "");
                ExportOne(device, parentId, Listing(parentId), item, result, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail(item, result, ex.Message, FileErrors.Classify(ex));
            }

            progress.EndFile();
        }

        if (_options.Move && !_options.DryRun)
        {
            // Las carpetas de origen que se han quedado vacías desaparecen, como al mover con el Explorador.
            foreach (string source in request.Sources.Where(Directory.Exists))
            {
                DeleteEmptyDirectories(source);
            }
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(LogLevel.Info, Texts.F("E_DoneCopy", result.Elapsed.ToString(@"hh\:mm\:ss"), result.FilesCopied, result.FilesSkipped, result.Failures.Count));
        return result;
    }

    private void ExportOne(IRemoteStore device, string parentId, List<RemoteItem> siblings, TransferItem item, CopyJobResult result, ProgressTracker progress, CancellationToken cancellationToken)
    {
        string name = RemotePath.Name(item.Destination);
        var existing = siblings.FirstOrDefault(o => !o.IsFolder && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var action = DecideMtpConflict(existing.Size, existing.ModifiedUtc, item.Length, item.LastWriteUtc, sizeOnly: true, out string? reason);
            switch (action)
            {
                case MtpAction.Skip:
                    result.FilesSkipped++;
                    Files.Update(item, FileState.Skipped, reason);
                    if (_options.Move && !_options.DryRun && reason == Texts.Get("E_SkipIdentical"))
                    {
                        File.Delete(item.Source);
                    }

                    return;
                case MtpAction.Fail:
                    Fail(item, result, Texts.F("E_DestinationExists", RemotePath.Display(item.Destination)));
                    return;
                case MtpAction.KeepBoth or MtpAction.RenameExisting:
                    // En el móvil no se renombra lo que ya hay: el nuevo llega con otro nombre.
                    name = Path.GetFileName(FreeName(Path.Combine("x", name), candidate =>
                        !siblings.Any(o => string.Equals(o.Name, Path.GetFileName(candidate), StringComparison.OrdinalIgnoreCase))));
                    item.Destination = RemotePath.Combine(RemotePath.Parent(item.Destination), name);
                    existing = null;
                    break;
            }
        }

        if (_options.DryRun)
        {
            result.FilesCopied++;
            Files.Update(item, FileState.Planned, Texts.Get(existing is null ? "E_WouldCopy" : "E_WouldReplace"));
            return;
        }

        if (existing is not null)
        {
            device.Delete(existing.Id, recursive: false);
            siblings.Remove(existing);
        }

        var hasher = FileHasher.Create(_options.HashAlgorithm == FileHashAlgorithm.None ? FileHashAlgorithm.XxHash3 : _options.HashAlgorithm)!;
        string id;
        using (var input = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
        using (var counting = new ProgressStream(input, count =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.AddTransferred(count);
        }, hasher))
        {
            id = device.Upload(parentId, name, counting, item.Length, cancellationToken);
        }

        item.SourceHash = hasher.Finish();
        siblings.Add(new RemoteItem(id, name, false, item.Length, DateTime.UtcNow));

        if (_options.Verify && id.Length > 0)
        {
            item.DestinationHash = HashDeviceObject(device, id);
            if (!string.Equals(item.DestinationHash, item.SourceHash, StringComparison.OrdinalIgnoreCase))
            {
                TryMtp(() => device.Delete(id, recursive: false));
                Fail(item, result, Texts.Get("E_HashMismatch"), FileErrorKind.ChecksumMismatch);
                return;
            }
        }

        result.FilesCopied++;
        result.BytesTransferred += item.Length;
        Files.Update(item, _options.Verify && id.Length > 0 ? FileState.Verified : FileState.Copied);
        if (_options.Move)
        {
            File.Delete(item.Source);
        }
    }

    /// <summary>
    /// Qué hacer si el archivo ya está en el destino, con la política de conflictos de la copia. Idéntico = mismo tamaño
    /// y fecha dentro de la tolerancia (o sin fecha que comparar; en el móvil, solo el tamaño).
    /// "Preguntar" no tiene ventana a la que preguntar aquí: se conservan los dos.
    /// </summary>
    private MtpAction DecideMtpConflict(long existingLength, DateTime? existingUtc, long sourceLength, DateTime? sourceUtc, bool sizeOnly, out string? reason)
    {
        reason = null;
        bool datesKnown = !sizeOnly && existingUtc is not null && sourceUtc is not null && sourceUtc.Value > DateTime.MinValue;
        bool identical = existingLength == sourceLength && (!datesKnown || (existingUtc!.Value - sourceUtc!.Value).Duration() <= _options.TimeTolerance);
        if (identical && !_options.ForceRecopy)
        {
            reason = Texts.Get("E_SkipIdentical");
            return MtpAction.Skip;
        }

        switch (_options.Conflict)
        {
            case ConflictPolicy.Skip:
                reason = Texts.Get("E_SkipExisted");
                return MtpAction.Skip;
            case ConflictPolicy.OverwriteIfNewer when datesKnown && sourceUtc <= existingUtc:
                reason = Texts.Get("E_SkipOlder");
                return MtpAction.Skip;
            case ConflictPolicy.OverwriteIfDifferentSize when existingLength == sourceLength:
                reason = Texts.Get("E_SkipSameSize");
                return MtpAction.Skip;
            case ConflictPolicy.Fail:
                return MtpAction.Fail;
            case ConflictPolicy.KeepBoth or ConflictPolicy.Ask:
                return MtpAction.KeepBoth;
            case ConflictPolicy.RenameExisting:
                return MtpAction.RenameExisting;
            default:
                return MtpAction.Replace;
        }
    }

    private void TryMtp(Action action)
    {
        try
        {
            action();
        }
        catch (IOException ex)
        {
            Log(LogLevel.Warning, ex.Message);
        }
    }

    private static void DeleteEmptyDirectories(string directory)
    {
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            DeleteEmptyDirectories(child);
        }

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    /// <summary>Los móviles admiten nombres que Windows no (":", "?"...): se cambian por "_".</summary>
    private static string SafeName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.TrimEnd('.', ' ') is { Length: > 0 } trimmed ? trimmed : "_";
    }

    /// <summary>Envuelve la lectura del archivo local para contar el progreso y calcular el hash mientras se sube.</summary>
    private sealed class ProgressStream(Stream inner, Action<int> onRead, FileHasher hasher) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            if (read > 0)
            {
                hasher.Append(buffer.AsSpan(offset, read));
                onRead(read);
            }

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
