using System.Runtime.InteropServices;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>Por qué MegaCopier pausó solo las copias.</summary>
public enum AutoPauseReason
{
    None,
    Battery,
    FullScreen,
}

/// <summary>
/// Que copiar no estorbe: pausa con batería y durante juegos o presentaciones a pantalla completa (y reanuda después,
/// solo lo que pausó él), y calcula el límite de velocidad temporal (mientras usas el equipo y por horario).
/// </summary>
internal sealed partial class Comfort(App app)
{
    private readonly HashSet<CopyJob> _autoPaused = [];
    private AutoPauseReason _reason;

    private AppSettings Settings => app.Settings;

    /// <summary>Lo que hay que respetar ahora (se llama dos veces por segundo).</summary>
    public void Tick(List<CopyJob> running)
    {
        var reason = Settings.PauseOnBattery && OnBattery() ? AutoPauseReason.Battery
            : Settings.PauseOnFullScreen && FullScreen() ? AutoPauseReason.FullScreen
            : AutoPauseReason.None;

        if (reason != AutoPauseReason.None)
        {
            var toPause = running.Where(j => j.State == CopyJobState.Running).ToList();
            foreach (var job in toPause)
            {
                app.Queue.Pause(job);
                _autoPaused.Add(job);
            }

            if (toPause.Count > 0 && reason != _reason)
            {
                app.Notify(L.Get("Comfort_PausedTitle"), L.Get("Comfort_Paused" + reason));
                Log($"pausa: {reason} ({toPause.Count})");
            }
        }
        else if (_autoPaused.Count > 0)
        {
            // Pasó el motivo: se reanuda lo que pausó MegaCopier (lo que pausaste tú sigue en pausa).
            foreach (var job in _autoPaused.Where(j => j.State == CopyJobState.Paused).ToList())
            {
                app.Queue.Resume(job);
            }

            Log($"reanudar ({_autoPaused.Count})");
            _autoPaused.Clear();
        }

        _reason = reason;
    }

    /// <summary>Por qué va más despacio ahora: "Active" (lo estás usando), "Schedule" (horario) o "".</summary>
    public string SlowReason { get; private set; } = "";

    /// <summary>Lo pausó MegaCopier (y por qué), para decirlo en la ventana.</summary>
    public AutoPauseReason PausedBy(CopyJob job) => _autoPaused.Contains(job) ? _reason : AutoPauseReason.None;

    /// <summary>Límite temporal en bytes/s (el más estricto de "mientras usas el equipo" y "por horario"), o null.</summary>
    public long? SpeedLimit()
    {
        long? limit = null;
        SlowReason = "";
        if (Settings.SlowWhenActiveMBps > 0 && SystemActions.IdleTime() < TimeSpan.FromSeconds(10))
        {
            limit = Settings.SlowWhenActiveMBps * (long)CopyOptions.MiB;
            SlowReason = "Active";
        }

        if (Settings.ScheduleLimitMBps > 0 && InSchedule(DateTime.Now.TimeOfDay, Settings.ScheduleFrom, Settings.ScheduleTo))
        {
            long scheduled = Settings.ScheduleLimitMBps * (long)CopyOptions.MiB;
            limit = limit is { } l ? Math.Min(l, scheduled) : scheduled;
            SlowReason = limit == scheduled ? "Schedule" : SlowReason;
        }

        return limit;
    }

    /// <summary>Dentro del horario [desde, hasta); si "hasta" es antes que "desde", el horario cruza la medianoche.</summary>
    public static bool InSchedule(TimeSpan now, TimeSpan from, TimeSpan to) =>
        from == to || (from < to ? now >= from && now < to : now >= from || now < to);

    private static bool OnBattery()
    {
        if (App.TestMode && TestPower() is { } simulated)
        {
            return simulated == "battery";
        }

        // ACLineStatus 0 = desenchufado (1 = enchufado, 255 = desconocido, como en un equipo de sobremesa).
        return GetSystemPowerStatus(out var status) && status.ACLineStatus == 0;
    }

    private static bool FullScreen()
    {
        if (App.TestMode && TestPower() is { } simulated)
        {
            return simulated == "fullscreen";
        }

        // QUNS_RUNNING_D3D_FULL_SCREEN = 3 (juego), QUNS_PRESENTATION_MODE = 4.
        return SHQueryUserNotificationState(out int state) == 0 && state is 3 or 4;
    }

    /// <summary>Solo pruebas: "battery", "fullscreen" o "ac" en test-power.txt simula el estado del equipo.</summary>
    private static string? TestPower()
    {
        try
        {
            string path = Path.Combine(DataFolder.Path, "test-power.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void Log(string line)
    {
        if (App.TestMode)
        {
            App.TestLog("comfort.log", line);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out PowerStatus status);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
