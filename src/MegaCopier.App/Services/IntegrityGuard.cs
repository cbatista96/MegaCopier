using MegaCopier.Engine;
using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>Una carpeta vigilada por el guardián de integridad.</summary>
public sealed class GuardedFolder
{
    public string Path { get; set; } = "";

    /// <summary>Última comprobación completa (null = todavía ninguna: se protege en cuanto se pueda).</summary>
    public DateTime? LastCheckUtc { get; set; }

    /// <summary>Archivos dañados en la última comprobación.</summary>
    public int Damaged { get; set; }
}

/// <summary>
/// Guardián de integridad: cada cierto tiempo, con el equipo libre, comprueba las carpetas elegidas contra el hash que
/// cada archivo guarda dentro de sí. Distingue un archivo que editaste (fecha nueva: se renueva su hash) de uno que se
/// dañó solo en el disco (misma fecha, otro contenido) y avisa. La primera vez, protege la carpeta.
/// </summary>
internal sealed class IntegrityGuard(App app)
{
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private DateTime _lastLook = DateTime.MinValue;

    private AppSettings Settings => app.Settings;

    /// <summary>Lo llama el temporizador de estado: como mucho una vez por minuto mira si toca alguna carpeta.</summary>
    public void Tick(bool busy)
    {
        if (Settings.GuardedFolders.Count == 0 || busy || DateTime.UtcNow - _lastLook < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _lastLook = DateTime.UtcNow;
        if (_running.Count > 0 || SystemActions.IdleTime() < TimeSpan.FromMinutes(2))
        {
            return; // solo con el equipo libre: no molesta mientras lo usas
        }

        var interval = TimeSpan.FromDays(Math.Max(1, Settings.GuardIntervalDays));
        if (Settings.GuardedFolders.FirstOrDefault(f => Directory.Exists(f.Path) && (f.LastCheckUtc is not { } last || DateTime.UtcNow - last >= interval)) is { } due)
        {
            Check(due);
        }
    }

    /// <summary>Comprueba (y protege lo nuevo) una carpeta ahora.</summary>
    public CopyJob? Check(GuardedFolder folder)
    {
        if (!Directory.Exists(folder.Path))
        {
            return null;
        }

        var options = Settings.NewJobOptions(new IpcRequest { Command = "copy" });
        options.Operation = JobOperation.Test;
        options.SaveHashStream = true; // renueva lo editado y protege lo que no tenía hash
        options.Unattended = true;
        options.CheckFreeSpace = false;
        options.Move = false;
        if (options.HashAlgorithm == FileHashAlgorithm.None)
        {
            options.HashAlgorithm = FileHashAlgorithm.XxHash3;
        }

        var job = app.Queue.Submit([folder.Path], folder.Path, options);
        _running.Add(job.Id);
        return job;
    }

    /// <summary>Al terminar una comprobación del guardián: se apunta y, si hay daños, se avisa.</summary>
    public bool OnFinished(CopyJob job)
    {
        if (!_running.Remove(job.Id))
        {
            return false;
        }

        if (Settings.GuardedFolders.FirstOrDefault(f => string.Equals(f.Path, job.Record.Sources.FirstOrDefault(), StringComparison.OrdinalIgnoreCase)) is { } folder)
        {
            int damaged = job.Result?.Failures.Count(f => f.Kind == FileErrorKind.ChecksumMismatch) ?? 0;
            folder.LastCheckUtc = DateTime.UtcNow;
            folder.Damaged = damaged;
            Settings.Save();
            if (damaged > 0)
            {
                string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(folder.Path));
                app.Notify(L.Get("Guard_DamagedTitle"), damaged == 1 ? L.F("Guard_DamagedOne", name) : L.F("Guard_DamagedMany", damaged, name));
            }

            if (App.TestMode)
            {
                App.TestLog("guard.log", $"{folder.Path} dañados={damaged} correctos={job.Result?.FilesCopied} omitidos={job.Result?.FilesSkipped}");
            }
        }

        return true;
    }
}
