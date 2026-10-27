using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>Un Ctrl+V del Explorador que MegaCopier se queda.</summary>
public sealed record PasteRequest(IReadOnlyList<string> Sources, string Destination, ClipboardOperation Operation)
{
    public bool DuplicateInPlace { get; init; }
    internal IReadOnlyList<string>? ClipboardSources { get; init; }
}

/// <summary>
/// Sustituye el Ctrl+V del Explorador por MegaCopier sin inyectar nada en el Explorador.
///
/// Un gancho de teclado de bajo nivel mira cada Ctrl+V. Solo lo retiene si en ese momento el foco está en la lista
/// de archivos de una ventana del Explorador (o del Escritorio) y el portapapeles contiene archivos: comprobaciones
/// instantáneas, porque Windows desactiva los ganchos lentos. El resto (leer el portapapeles, averiguar la carpeta
/// por COM) se hace en un hilo aparte. Las copias en el mismo sitio se duplican y los movimientos sobre sí mismos
/// no hacen nada. Solo los destinos que no son carpetas del disco se devuelven al Explorador.
/// </summary>
public sealed unsafe partial class PasteInterceptor : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104;
    private const int VkV = 0x56, VkControl = 0x11, VkShift = 0x10, VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;
    private const uint WmCommand = 0x0111;

    /// <summary>Orden "Pegar" de la vista de carpeta del Explorador (FCIDM_SHVIEW_INSERT).</summary>
    private const int ShellViewPaste = 0x701A;

    private static PasteInterceptor? _active;

    private readonly Func<PasteRequest, bool> _handler;
    private readonly BlockingCollection<nint> _work = new();
    private readonly Thread _worker;
    private readonly HookThread _hooks;
    private readonly ExplorerPasteMenu _menu;
    private uint _swallowedPasteKey;

    /// <param name="handler">
    /// Recibe la petición en el hilo de trabajo. Devuelve true si MegaCopier la acepta; false para que pegue el Explorador.
    /// </param>
    public PasteInterceptor(Func<PasteRequest, bool> handler)
    {
        _handler = handler;
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "MegaCopier.Paste" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
        _hooks = new HookThread(WhKeyboardLl, &HookProc, "MegaCopier.KeyboardHook");
        _hooks.Reinstalled += result => PassedThrough?.Invoke($"Windows había quitado el gancho del teclado: {result}");
        _menu = new ExplorerPasteMenu(HandleMenuPaste, line => Traced?.Invoke(line));
    }

    /// <summary>Se dispara (en el hilo de trabajo) cuando un pegado se devuelve al Explorador, con el motivo.</summary>
    public event Action<string>? PassedThrough;

    public event Action<string>? Traced;

    public bool IsEnabled => _hooks.IsInstalled;

    /// <summary>Instala el gancho, en un hilo propio (ver <see cref="HookThread"/>).</summary>
    public void Enable()
    {
        _active = this;
        _hooks.Start();
        _menu.Enable();
    }

    public void Disable()
    {
        _menu.Disable();
        _hooks.Stop();
    }

    public void Dispose()
    {
        Disable();
        _menu.Dispose();
        _work.CompleteAdding();
    }

    /// <summary>
    /// ¿Hay que quedarse con este Ctrl+V? Devuelve la ventana con el foco (la vista del Explorador) o 0.
    /// Solo comprobaciones baratas: se ejecuta dentro del gancho.
    /// </summary>
    internal static nint ShouldIntercept(bool shiftInsert = false)
    {
        if ((!shiftInsert && Pressed(VkShift)) || Pressed(VkMenu) || Pressed(VkLWin) || Pressed(VkRWin))
        {
            return 0;
        }

        nint foreground = Native.GetForegroundWindow();
        string topClass = Native.ClassName(foreground);
        if (topClass is not ("CabinetWClass" or "Progman" or "WorkerW"))
        {
            return 0;
        }

        nint focus = Native.DesktopFileFocus(foreground, Native.FocusedWindow(foreground));
        string focusClass = Native.ClassName(focus);

        // Una vista vacía recién activada puede dejar el foco en CabinetWClass, no en DirectUIHWND.
        // La pestaña visible se resuelve en el STA de trabajo, nunca consultando UIA dentro del gancho.
        if (topClass == "CabinetWClass" && (focus == 0 || focus == foreground))
            return ClipboardFiles.HasFiles() ? foreground : 0;

        // Solo la lista de archivos: nunca la barra de direcciones, la búsqueda o el cuadro de renombrar.
        if (focusClass is not ("DirectUIHWND" or "SysListView32" or "SHELLDLL_DefView"))
        {
            return 0;
        }

        if (Native.FindAncestorOfClass(focus, "SHELLDLL_DefView") == 0)
        {
            return 0;
        }

        return ClipboardFiles.HasFiles() ? focus : 0;
    }

    private static bool Pressed(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        var self = _active;
        self?._hooks.Touch();
        if (code >= 0 && self is not null)
        {
            try
            {
                var data = (KbdLlHookStruct*)lParam;
                if (data->dwExtraInfo is 0x4D435059 or 0x4D435050) return CallNextHookEx(0, code, wParam, lParam);
                uint message = (uint)wParam;
                if (IsKeyDownMessage(message))
                {
                    bool ctrl = Pressed(VkControl), shift = Pressed(VkShift);
                    if (data->vkCode == 0x5D /* Apps */ || (data->vkCode == 0x79 /* F10 */ && shift && !ctrl))
                        self._menu.BeginKeyboardContext();

                    if (IsPasteShortcut(data->vkCode, ctrl, shift, Pressed(VkMenu), Pressed(VkLWin) || Pressed(VkRWin)))
                    {
                        if (self._swallowedPasteKey == data->vkCode) return 1;
                        if (self._menu.TryHandleKey((ushort)data->vkCode, pasteShortcut: true))
                        {
                            self._swallowedPasteKey = data->vkCode;
                            return 1;
                        }
                        nint focus = ShouldIntercept(shiftInsert: data->vkCode == 0x2D);
                        if (focus != 0)
                        {
                            self._swallowedPasteKey = data->vkCode;
                            self._work.TryAdd(focus);
                            return 1; // el Explorador no ve este Ctrl+V
                        }
                    }
                    else if (data->vkCode is 0x0D or 0x20 && self._menu.TryHandleKey((ushort)data->vkCode, pasteShortcut: false)) return 1;
                }
                else if (message == WmKeyUp && self._swallowedPasteKey == data->vkCode)
                {
                    self._swallowedPasteKey = 0;
                    return 1;
                }
            }
            catch
            {
                // Una excepción no puede cruzar a código nativo; ante la duda, que el teclado funcione como siempre.
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    internal static bool IsPasteShortcut(uint key, bool ctrl, bool shift, bool alt, bool windows) =>
        !alt && !windows && ((key == VkV && ctrl && !shift) || (key == 0x2D && shift && !ctrl));

    // F10 abre el menú con WM_SYSKEYDOWN, incluso sin Alt; no basta WM_KEYDOWN.
    internal static bool IsKeyDownMessage(uint message) => message is WmKeyDown or WmSysKeyDown;

    private void WorkLoop()
    {
        foreach (nint focus in _work.GetConsumingEnumerable())
        {
            string? reason;
            PasteRequest? request = null;
            try
            {
                reason = Decide(focus, out request);
            }
            catch (Exception ex)
            {
                reason = ex.Message; // una consulta COM fallida no debe matar el hilo de todos los pegados siguientes
            }
            if (reason is null && request is null)
            {
                Traced?.Invoke("Pegado atendido: los archivos cortados ya están en su destino");
                continue;
            }
            if (reason is null && request is not null)
            {
                try
                {
                    if (_handler(request))
                    {
                        Traced?.Invoke($"Ctrl+V: {request.Sources.Count} elemento(s) a {request.Destination} ({request.Operation})");
                        // Como el Explorador: tras pegar algo cortado, el portapapeles se vacía (ya no está en su sitio).
                        if (request.Operation == ClipboardOperation.Move)
                        {
                            ClipboardFiles.ClearIfSame(request.ClipboardSources ?? request.Sources);
                        }

                        continue;
                    }

                    reason = "MegaCopier no aceptó la copia";
                }
                catch (Exception ex)
                {
                    reason = ex.Message;
                }
            }

            PasteInExplorer(focus);
            PassedThrough?.Invoke(reason ?? "desconocido");
        }
    }

    private void HandleMenuPaste(PasteRequest request)
    {
        if (_handler(request))
        {
            if (request.Operation == ClipboardOperation.Move)
            {
                ClipboardFiles.ClearIfSame(request.ClipboardSources ?? request.Sources);
            }

            Traced?.Invoke($"Pegar del menú: {request.Sources.Count} elemento(s) a {request.Destination}");
        }
        else
        {
            Traced?.Invoke("Pegar del menú: MegaCopier no aceptó la petición después de cerrar el menú");
        }
    }

    private static string? Decide(nint focus, out PasteRequest? request)
    {
        nint view = Native.ClassName(focus) == "CabinetWClass" ? UiaHitTest.ActiveFileView(focus) : focus;
        return PasteDecision.Decide(ClipboardFiles.TryRead(), view == 0 ? null : ExplorerLocator.GetFolderPath(view), out request);
    }

    /// <summary>Devuelve el pegado al Explorador: la misma orden que su menú "Pegar".</summary>
    private static void PasteInExplorer(nint focus)
    {
        nint view = Native.FindAncestorOfClass(focus, "SHELLDLL_DefView");
        if (view == 0 && Native.ClassName(focus) == "CabinetWClass") view = UiaHitTest.ActiveFileView(focus);
        if (view != 0)
        {
            Native.PostMessageW(view, WmCommand, ShellViewPaste, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
}
