using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine;

/// <summary>Un archivo en uso que hay que copiar desde una instantánea.</summary>
public sealed record LockedFile(string Source, string Destination);

/// <summary>Lo que se pide al proceso con permisos de administrador y lo que contesta.</summary>
public sealed class LockedFilesRequest
{
    public List<LockedFile> Files { get; set; } = [];

    /// <summary>Errores por archivo (vacío = copiado).</summary>
    public Dictionary<string, string> Errors { get; set; } = [];

    public bool Done { get; set; }
}

[JsonSerializable(typeof(LockedFilesRequest))]
internal sealed partial class LockedFilesJsonContext : JsonSerializerContext;

/// <summary>Crea y borra instantáneas de volumen (VSS).</summary>
public interface ISnapshotProvider
{
    /// <summary>Crea la instantánea del volumen (por ejemplo "C:\") y devuelve su raíz para leer, y su id.</summary>
    (string Root, string Id) Create(string volume);

    void Delete(string id);
}

/// <summary>
/// Copia archivos que otro programa tiene bloqueados leyéndolos de una instantánea de volumen (VSS), como los
/// programas de copia de seguridad. Necesita permisos de administrador: lo hace un proceso aparte que pide permiso a
/// Windows (el aviso de control de cuentas) y deja el resultado en el mismo archivo de petición.
/// </summary>
public static class LockedFiles
{
    public static string Save(LockedFilesRequest request, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(request, LockedFilesJsonContext.Default.LockedFilesRequest));
        return path;
    }

    public static LockedFilesRequest? Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), LockedFilesJsonContext.Default.LockedFilesRequest);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Hace el trabajo (en el proceso con permisos): una instantánea por volumen, copia y la borra.</summary>
    public static void Copy(LockedFilesRequest request, ISnapshotProvider snapshots, CopyOptions options)
    {
        foreach (var group in request.Files.GroupBy(f => Path.GetPathRoot(Path.GetFullPath(f.Source))!, StringComparer.OrdinalIgnoreCase))
        {
            string? id = null;
            try
            {
                (string root, id) = snapshots.Create(group.Key);
                foreach (var file in group)
                {
                    try
                    {
                        string relative = Path.GetFullPath(file.Source)[group.Key.Length..];
                        string shadow = Path.Combine(root, relative);
                        var copy = new CopyEngine(Clone(options));
                        var result = copy.Run(new CopyJobRequest([shadow], Path.GetDirectoryName(file.Destination)!));
                        if (!result.Success)
                        {
                            request.Errors[file.Source] = result.Failures[0].Message;
                        }
                        else
                        {
                            // Las fechas del original (la instantánea las conserva), con el nombre del destino previsto.
                            string copied = Path.Combine(Path.GetDirectoryName(file.Destination)!, Path.GetFileName(shadow));
                            if (!string.Equals(copied, file.Destination, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Move(copied, file.Destination, overwrite: true);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CopyPlanException)
                    {
                        request.Errors[file.Source] = ex.Message;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                foreach (var file in group)
                {
                    request.Errors[file.Source] = $"No se pudo crear la instantánea de {group.Key}: {ex.Message}";
                }
            }
            finally
            {
                if (id is not null)
                {
                    try
                    {
                        snapshots.Delete(id);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException)
                    {
                        Debug.WriteLine($"No se pudo borrar la instantánea {id}: {ex.Message}");
                    }
                }
            }
        }

        request.Done = true;
    }

    private static CopyOptions Clone(CopyOptions options) => new()
    {
        Conflict = ConflictPolicy.Overwrite,
        Verify = options.Verify,
        HashAlgorithm = options.HashAlgorithm,
        PreserveTimestamps = options.PreserveTimestamps,
        PreserveAttributes = options.PreserveAttributes,
        CopyStreams = options.CopyStreams,
        CopySecurity = options.CopySecurity,
        Unattended = true,
        CheckFreeSpace = false,
        RetryCount = 1,
        ProgressInterval = TimeSpan.FromSeconds(1),
    };
}

/// <summary>Instantáneas reales con WMI (Win32_ShadowCopy) a través de PowerShell. Necesita administrador.</summary>
public sealed class WmiSnapshotProvider : ISnapshotProvider
{
    public (string Root, string Id) Create(string volume)
    {
        string script =
            $"$r = Invoke-CimMethod -ClassName Win32_ShadowCopy -MethodName Create -Arguments @{{ Volume = '{volume.Replace("'", "''")}'; Context = 'ClientAccessible' }}; " +
            "if ($r.ReturnValue -ne 0) { throw \"Win32_ShadowCopy.Create devolvió $($r.ReturnValue)\" }; " +
            "$s = Get-CimInstance Win32_ShadowCopy -Filter \"ID='$($r.ShadowID)'\"; " +
            "Write-Output \"$($s.DeviceObject)|$($r.ShadowID)\"";
        string output = PowerShell(script).Trim();
        int bar = output.LastIndexOf('|');
        if (bar <= 0)
        {
            throw new InvalidOperationException(output.Length > 0 ? output : "Sin respuesta de Win32_ShadowCopy.");
        }

        // \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN\ se lee como cualquier carpeta.
        return (output[..bar] + "\\", output[(bar + 1)..]);
    }

    public void Delete(string id) =>
        PowerShell($"Get-CimInstance Win32_ShadowCopy -Filter \"ID='{id.Replace("'", "''")}'\" | Remove-CimInstance");

    private static string PowerShell(string script)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(script);
        using var process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(error.Trim().Length > 0 ? error.Trim() : $"PowerShell terminó con {process.ExitCode}");
        }

        return output;
    }
}
