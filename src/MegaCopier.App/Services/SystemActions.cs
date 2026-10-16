using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MegaCopier.App.Services;

/// <summary>Qué hacer cuando terminan todas las copias (solo para la sesión actual).</summary>
public enum FinishAction
{
    None,
    Sleep,
    Shutdown,
    Hibernate,
    SignOut,

    /// <summary>Cerrar MegaCopier (la bandeja también).</summary>
    ExitApp,
}

internal static partial class SystemActions
{
    public static void Run(FinishAction action)
    {
        if (App.TestMode)
        {
            // En pruebas nunca se apaga ni se suspende el equipo: solo se anota.
            App.TestLog("finish.log", action.ToString());
            return;
        }

        switch (action)
        {
            case FinishAction.Sleep:
                SetSuspendState(hibernate: false, force: false, disableWakeEvent: false);
                break;
            case FinishAction.Hibernate:
                SetSuspendState(hibernate: true, force: false, disableWakeEvent: false);
                break;
            case FinishAction.SignOut:
                ExitWindowsEx(0 /* EWX_LOGOFF */, 0x00040000 /* SHTDN_REASON_FLAG_PLANNED */);
                break;
            case FinishAction.Shutdown:
                // Con un minuto de margen: se puede cancelar con "shutdown /a".
                Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 60 /c \"MegaCopier terminó las copias. El equipo se apagará en 1 minuto.\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                break;
        }
    }

    /// <summary>Sonido del sistema al terminar: el de información si fue bien, el de error si no.</summary>
    public static void PlayFinishSound(bool ok) => MessageBeep(ok ? 0x40u /* MB_ICONASTERISK */ : 0x10u /* MB_ICONHAND */);

    /// <summary>
    /// Ejecuta los comandos "al terminar" de un trabajo (uno por línea) con cmd.exe, sin ventana. Reciben el trabajo en
    /// variables de entorno: MEGACOPIER_STATE, MEGACOPIER_SOURCES, MEGACOPIER_DESTINATION, MEGACOPIER_FILES,
    /// MEGACOPIER_BYTES, MEGACOPIER_FAILED y MEGACOPIER_CHECKSUM. Con "ok:" delante, solo si fue bien; con "error:", solo si no.
    /// </summary>
    public static void RunScripts(IReadOnlyList<string> scripts, IReadOnlyDictionary<string, string> environment, bool succeeded)
    {
        foreach (string line in scripts.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (ScriptFor(line, succeeded) is not { } script)
            {
                continue;
            }


            try
            {
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c {script}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = environment.TryGetValue("MEGACOPIER_DESTINATION", out string? folder) && Directory.Exists(folder)
                        ? folder
                        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                };
                foreach (var (key, value) in environment)
                {
                    info.Environment[key] = value;
                }

                Process.Start(info)?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Debug.WriteLine($"Script fallido: {script}: {ex.Message}");
            }
        }
    }

    /// <summary>El comando de una línea si toca ejecutarlo ("ok:" solo si fue bien, "error:" solo si no; sin prefijo, siempre).</summary>
    internal static string? ScriptFor(string line, bool succeeded)
    {
        string trimmed = line.Trim();
        foreach (var (prefix, when) in new[] { ("ok:", true), ("error:", false) })
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return succeeded == when ? trimmed[prefix.Length..].Trim() : null;
            }
        }

        return trimmed;
    }

    private static bool _background;

    /// <summary>
    /// Prioridad baja de verdad: además de la CPU, el disco y la memoria pasan a prioridad de segundo plano
    /// (PROCESS_MODE_BACKGROUND), así la copia no entorpece lo que esté haciendo el usuario.
    /// </summary>
    public static void SetBackgroundMode(bool background)
    {
        if (background != _background)
        {
            _background = background;
            SetPriorityClass(GetCurrentProcess(), background ? 0x00100000u /* PROCESS_MODE_BACKGROUND_BEGIN */ : 0x00200000u /* _END */);
        }
    }

    /// <summary>Tiempo desde la última vez que el usuario tocó el teclado o el ratón.</summary>
    public static TimeSpan IdleTime()
    {
        // Pruebas: simular que el usuario está (o no) usando el equipo sin tocar el teclado ni el ratón.
        if (App.TestMode && double.TryParse(Environment.GetEnvironmentVariable("MEGACOPIER_TEST_IDLE"), System.Globalization.CultureInfo.InvariantCulture, out double seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        var info = new LastInputInfo { Size = 8 };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.MaxValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetPriorityClass(nint process, uint priorityClass);

    private static bool _keepingAwake;

    /// <summary>
    /// Mientras hay copias en marcha el equipo no se suspende por inactividad (la pantalla sí puede apagarse).
    /// Debe llamarse siempre desde el mismo hilo.
    /// </summary>
    public static void KeepAwake(bool awake)
    {
        if (awake != _keepingAwake)
        {
            _keepingAwake = awake;
            SetThreadExecutionState(awake ? 0x80000001u /* ES_CONTINUOUS | ES_SYSTEM_REQUIRED */ : 0x80000000u /* ES_CONTINUOUS */);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint type);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ExitWindowsEx(uint flags, uint reason);

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool force,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);
}

/// <summary>Progreso en el botón de la barra de tareas (ITaskbarList3), con llamadas COM directas compatibles con AOT.</summary>
internal sealed unsafe partial class TaskbarProgress : IDisposable
{
    public const int NoProgress = 0, Indeterminate = 1, Normal = 2, Error = 4, Paused = 8;

    private readonly nint _hwnd;
    private void* _taskbar;
    private int _state = -1;

    public TaskbarProgress(nint hwnd)
    {
        _hwnd = hwnd;
        Guid clsid = new("56FDF344-FD6D-11D0-958A-006097C9A090");
        Guid iid = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");
        void* taskbar = null;
        if (CoCreateInstance(&clsid, null, 1 /* CLSCTX_INPROC_SERVER */, &iid, &taskbar) >= 0 && taskbar != null)
        {
            // ITaskbarList::HrInit (índice 3)
            ((delegate* unmanaged[Stdcall]<void*, int>)(*(void***)taskbar)[3])(taskbar);
            _taskbar = taskbar;
        }
    }

    public void Update(int state, ulong completed, ulong total)
    {
        if (_taskbar == null)
        {
            return;
        }

        if (state != _state)
        {
            // ITaskbarList3::SetProgressState (índice 10)
            ((delegate* unmanaged[Stdcall]<void*, nint, int, int>)(*(void***)_taskbar)[10])(_taskbar, _hwnd, state);
            _state = state;
        }

        if (state is Normal or Paused or Error && total > 0)
        {
            // ITaskbarList3::SetProgressValue (índice 9)
            ((delegate* unmanaged[Stdcall]<void*, nint, ulong, ulong, int>)(*(void***)_taskbar)[9])(_taskbar, _hwnd, completed, total);
        }
    }

    public void Dispose()
    {
        if (_taskbar != null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)_taskbar)[2])(_taskbar);
            _taskbar = null;
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);
}
