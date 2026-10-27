using System.Diagnostics;
using System.Runtime.InteropServices;
using MegaCopier.App.Theming;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>
/// «Progreso de cada copia en la bandeja»: un icono más junto al de MegaCopier por cada copia en marcha, con su
/// porcentaje y una barra que sube. Al terminar bien se queda un momento lleno, con una marca, y desaparece. Un clic
/// abre la ventana en esa copia; el clic derecho, sus acciones (pausar, reanudar, detener).
/// </summary>
internal sealed partial class TrayProgressIcons(TrayIcon tray)
{
    /// <summary>Los números de los iconos extra: el principal es el 1.</summary>
    private const uint FirstId = 101, LastId = 110;

    /// <summary>Lo que se queda a la vista el icono lleno de una copia que acaba de terminar.</summary>
    private static readonly TimeSpan DoneVisible = TimeSpan.FromSeconds(1.6);

    private readonly Dictionary<CopyJob, uint> _ids = [];
    private readonly Dictionary<uint, string> _drawn = [];
    private readonly Dictionary<CopyJob, long> _doneAt = [];
    private (bool Light, long Read) _taskbar = (false, 0);

    /// <summary>La copia de un icono extra (null si ya no está).</summary>
    public CopyJob? JobFor(uint id) => _ids.FirstOrDefault(p => p.Value == id).Key;

    /// <summary>El número del icono de progreso de una copia (null si no tiene).</summary>
    public uint? IdFor(CopyJob job) => _ids.TryGetValue(job, out uint id) ? id : null;
    internal int Count => _ids.Count;

    /// <param name="jobs">Todas las copias de la cola.</param>
    /// <param name="enabled">Si está activado y el icono de la bandeja se muestra.</param>
    /// <param name="describe">Texto del icono (al pasar el ratón) de cada copia.</param>
    public void Update(IReadOnlyList<CopyJob> jobs, bool enabled, Func<CopyJob, string> describe, Func<CopyJob, bool> needsIcon)
    {
        long now = Stopwatch.GetTimestamp();
        foreach (var job in _ids.Keys.ToList())
        {
            bool running = job.State is CopyJobState.Running or CopyJobState.Paused;
            bool justDone = job.State is CopyJobState.Completed or CopyJobState.CompletedWithErrors
                && (!_doneAt.TryGetValue(job, out long at) || Stopwatch.GetElapsedTime(at) < DoneVisible);
            if (!enabled || !needsIcon(job) || (!running && !justDone) || !jobs.Contains(job) && !justDone)
            {
                Remove(job);
            }
            else if (justDone)
            {
                _doneAt.TryAdd(job, now);
            }
        }

        if (!enabled)
        {
            return;
        }

        foreach (var job in jobs.Where(j => j.State is CopyJobState.Running or CopyJobState.Paused && needsIcon(j)))
        {
            if (!_ids.ContainsKey(job) && FreeId() is { } id)
            {
                _ids[job] = id;
            }
        }

        int size = Math.Max(16, GetSystemMetrics(49 /* SM_CXSMICON */));
        bool light = LightTaskbar();
        var theme = ThemeManager.Current;
        foreach (var (job, id) in _ids)
        {
            bool done = _doneAt.ContainsKey(job);
            double fraction = job.Progress is { TotalBytes: > 0 } progress ? progress.Fraction : 0; // preparando: vacía
            var state = job.State == CopyJobState.Paused ? TrayProgressState.Paused
                : job.State == CopyJobState.CompletedWithErrors ? TrayProgressState.Error
                : TrayProgressState.Running;
            string tooltip = describe(job);

            // Solo se vuelve a dibujar si cambia lo que se ve (el número, el color, el tamaño o el tema).
            string key = $"{(done ? 100 : (int)Math.Floor(fraction * 100))}|{(int)(fraction * size)}|{state}|{size}|{light}|{theme.TotalStart}|{theme.TotalEnd}|{IconPacks.Current.Id}|{theme.Accent}|{tooltip}";
            if (_drawn.TryGetValue(id, out string? drawn) && drawn == key)
            {
                continue;
            }

            nint icon = TrayProgressPainter.Create(
                size, fraction, done, state, ThemeManager.Parse(theme.TotalStart), ThemeManager.Parse(theme.TotalEnd), light, IconPacks.Current);
            if (icon != 0)
            {
                tray.SetExtra(id, icon, tooltip);
                _drawn[id] = key;
            }
        }
    }

    /// <summary>Quita todos los iconos de progreso.</summary>
    public void Clear()
    {
        foreach (var job in _ids.Keys.ToList())
        {
            Remove(job);
        }
    }

    private void Remove(CopyJob job)
    {
        if (_ids.Remove(job, out uint id))
        {
            tray.RemoveExtra(id);
            _drawn.Remove(id);
        }

        _doneAt.Remove(job);
    }

    private uint? FreeId()
    {
        for (uint id = FirstId; id <= LastId; id++)
        {
            if (!_ids.ContainsValue(id))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>¿La barra de tareas es clara? (se vuelve a mirar cada pocos segundos, por si cambia el tema de Windows)</summary>
    private bool LightTaskbar()
    {
        if (_taskbar.Read == 0 || Stopwatch.GetElapsedTime(_taskbar.Read) > TimeSpan.FromSeconds(3))
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            _taskbar = (key?.GetValue("SystemUsesLightTheme") is int value && value != 0, Stopwatch.GetTimestamp());
        }

        return _taskbar.Light;
    }

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
