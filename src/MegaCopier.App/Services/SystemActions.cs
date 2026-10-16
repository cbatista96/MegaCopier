using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MegaCopier.App.Services;

/// <summary>Qué hacer cuando terminan todas las copias (solo para la sesión actual).</summary>
public enum FinishAction
{
    None,
    Sleep,
    Shutdown,
}

internal static partial class SystemActions
{
    public static void Run(FinishAction action)
    {
        switch (action)
        {
            case FinishAction.Sleep:
                SetSuspendState(hibernate: false, force: false, disableWakeEvent: false);
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
    /// MEGACOPIER_BYTES, MEGACOPIER_FAILED y MEGACOPIER_CHECKSUM.
    /// </summary>
    public static void RunScripts(IReadOnlyList<string> scripts, IReadOnlyDictionary<string, string> environment)
    {
        foreach (string script in scripts.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
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
