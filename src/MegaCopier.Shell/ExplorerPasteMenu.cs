using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Intercepta Pegar en el menú contextual de una vista del Explorador. El gancho solo retiene el botón al soltar
/// sobre un emergente del mismo Explorador; UI Automation y COM se consultan en un STA aparte con plazo de devolución.
/// No modifica menús, no inyecta DLL y no decide por el texto traducido de la orden.
/// </summary>
internal sealed unsafe partial class ExplorerPasteMenu : IDisposable
{
    private const nuint Marker = 0x4D435059;
    private static ExplorerPasteMenu? _active;
    private readonly HookThread _hooks;
    private readonly Action<PasteRequest> _handler;
    private readonly Action<string> _trace;
    private Context? _context;
    private nint _pressedPopup;
    private int _working;
    private readonly ExplorerPasteToolbar _toolbar;

    public ExplorerPasteMenu(Action<PasteRequest> handler, Action<string> trace)
    {
        _handler = handler;
        _trace = trace;
        _toolbar = new ExplorerPasteToolbar(handler, trace);
        _hooks = new HookThread(14 /* WH_MOUSE_LL */, &HookProc, "MegaCopier.PasteMenuHook");
    }

    public void Enable()
    {
        _active = this;
        _hooks.Start();
        _toolbar.Enable();
    }

    public void Disable()
    {
        _hooks.Stop();
        _toolbar.Disable();
        _context = null;
        _pressedPopup = 0;
    }

    public void Dispose()
    {
        Disable();
        _toolbar.Dispose();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        var self = _active;
        self?._hooks.Touch();
        if (code >= 0 && self is not null && self._hooks.IsInstalled)
        {
            try
            {
                var mouse = (MouseHook*)lParam;
                if (mouse->Extra != Marker && self.OnMouse((uint)wParam, mouse->X, mouse->Y))
                {
                    return 1;
                }
            }
            catch
            {
                // El ratón debe seguir funcionando aunque el Explorador cierre una ventana mientras se consulta.
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private bool OnMouse(uint message, int x, int y)
    {
        if (message is not (0x0204 or 0x0201 or 0x0202))
        {
            return false;
        }

        nint window = Native.WindowFromPoint(new Native.Point(x, y));
        if (_toolbar.OnMouse(message, x, y, window))
        {
            _context = null;
            _pressedPopup = 0;
            return true;
        }
        if (message == 0x0204 /* WM_RBUTTONDOWN */)
        {
            _pressedPopup = 0;
            nint view = Native.FindAncestorOfClass(window, "SHELLDLL_DefView");
            _context = view != 0 && ExplorerLocator.IsExplorerOrDesktop(view)
                ? new Context(view, Native.GetAncestor(view, 2), Stopwatch.GetTimestamp()) : null;
            return false;
        }

        var context = _context;
        if (context is null)
        {
            return false;
        }

        if (Stopwatch.GetElapsedTime(context.Started) > TimeSpan.FromSeconds(60))
        {
            _context = null;
            return false;
        }

        nint popup = PopupWindow(window);
        Native.GetWindowThreadProcessId(context.Root, out uint explorerPid);
        Native.GetWindowThreadProcessId(window, out uint targetPid);
        if (message == 0x0201 /* WM_LBUTTONDOWN */)
        {
            _pressedPopup = explorerPid == targetPid ? popup : 0;
            if (_pressedPopup == 0)
            {
                _context = null;
            }

            return false;
        }

        nint pressed = _pressedPopup;
        _pressedPopup = 0;
        if (popup == 0 || pressed != popup || explorerPid != targetPid || !ClipboardFiles.HasFiles()
            || Volatile.Read(ref _working) != 0)
        {
            return false;
        }

        Interlocked.Exchange(ref _working, 1);
        var click = new HeldClick();
        var thread = new Thread(() => HandleClick(context, popup, x, y, click))
        { IsBackground = true, Name = "MegaCopier.PasteMenu" };
        thread.SetApartmentState(ApartmentState.STA);
        click.Start();
        try
        {
            thread.Start();
        }
        catch
        {
            click.Dispose();
            Interlocked.Exchange(ref _working, 0);
            throw;
        }

        return true;
    }

    private void HandleClick(Context context, nint popup, int x, int y, HeldClick click)
    {
        try
        {
            if (!_hooks.IsInstalled || !UiaHitTest.IsPasteMenuAt(x, y, context.Root))
            {
                return;
            }

            var clipboard = ClipboardFiles.TryRead();
            var snapshot = ExplorerLocator.Snapshot(context.View, selection: true, items: false);
            string? destination = Destination(snapshot);
            if (clipboard is null || destination is null || !Directory.Exists(destination)
                || clipboard.Paths.Any(p => !File.Exists(p) && !Directory.Exists(p))
                || clipboard.Paths.Any(p => string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(p)),
                    Path.TrimEndingDirectorySeparator(destination), StringComparison.OrdinalIgnoreCase)))
            {
                return; // el menú original resuelve ubicaciones virtuales, accesos directos y copias sobre sí mismas
            }

            // Si cambió la aplicación activa o venció el plazo, no se cierra ningún menú ni se copia tarde.
            nint foreground = Native.GetForegroundWindow();
            if (!_hooks.IsInstalled || (foreground != context.Root && Native.GetAncestor(foreground, 3) != context.Root)
                || !IsWindowVisible(popup) || !click.Claim())
            {
                return;
            }

            if (!CloseMenu(popup, context.Root))
            {
                _trace("Pegar del menú devuelto al Explorador: no se confirmó el cierre del menú");
                return;
            }

            click.Release();
            _handler(new PasteRequest(clipboard.Paths, destination, clipboard.Operation));
        }
        catch (Exception ex)
        {
            _trace($"Pegar del menú: {ex.Message}");
        }
        finally
        {
            click.Dispose();
            UiaHitTest.ReleaseThread();
            Interlocked.Exchange(ref _working, 0);
        }
    }

    private static string? Destination(ViewSnapshot? snapshot) => snapshot?.Selection switch
    {
        { Count: 0 } => snapshot.Folder,
        { Count: 1 } selected when Directory.Exists(selected[0]) => selected[0],
        _ => null,
    };

    private static nint PopupWindow(nint window)
    {
        for (int i = 0; i < 8 && window != 0; i++, window = Native.GetParent(window))
        {
            string name = Native.ClassName(window);
            if (name == "#32768" || name.Contains("Popup", StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return 0;
    }

    private static bool CloseMenu(nint popup, nint owner)
    {
        // El menú clásico mantiene la captura mientras el botón está retenido y no siempre atiende Esc.
        // WM_CANCELMODE pide al propietario terminar su menú (comportamiento documentado de DefWindowProc).
        if (Native.ClassName(popup) == "#32768")
        {
            uint thread = Native.GetWindowThreadProcessId(popup, out _);
            nint menuOwner = Native.MenuOwner(thread);
            // Un mensaje publicado puede quedar esperando dentro del bucle modal de TrackPopupMenu.
            // Se envía al propietario real, desde este hilo de trabajo y con un plazo acotado.
            nuint result = 0;
            SendMessageTimeoutW(menuOwner != 0 ? menuOwner : owner, 0x001F /* WM_CANCELMODE */, 0, 0,
                0x0003 /* SMTO_BLOCK | SMTO_ABORTIFHUNG */, 250, &result);
            for (int i = 0; i < 30; i++)
            {
                if (!IsWindowVisible(popup))
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }

        if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0 || !Key(up: false))
        {
            return false;
        }

        try
        {
            for (int i = 0; i < 30; i++)
            {
                if (!IsWindowVisible(popup))
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }
        finally
        {
            Key(up: true);
        }
    }

    private sealed class HeldClick : IDisposable
    {
        private int _claimed, _released;
        private Timer? _timer;

        public void Start() => _timer = new Timer(_ =>
        {
            if (Interlocked.CompareExchange(ref _claimed, 1, 0) == 0)
            {
                Release();
            }
        }, null, 800, Timeout.Infinite);

        public bool Claim()
        {
            bool won = Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
            _timer?.Dispose();
            return won;
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                var input = new Input { Type = 0, Mouse = new MouseInput { Flags = 0x0004, Extra = Marker } };
                SendInput(1, &input, sizeof(Input));
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _claimed, 1);
            _timer?.Dispose();
            Release();
        }
    }

    private static bool Key(bool up)
    {
        var input = new Input { Type = 1, Key = new KeyboardInput { VirtualKey = 0x1B, Flags = up ? 2u : 0u, Extra = Marker } };
        return SendInput(1, &input, sizeof(Input)) == 1;
    }

    private sealed record Context(nint View, nint Root, long Started);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHook
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint Extra;
    }

    // Win64: DWORD seguido de la unión alineada a 8 bytes; MOUSEINPUT ocupa 32 bytes.
    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MouseInput Mouse;
        [FieldOffset(8)] public KeyboardInput Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public nuint Extra;
    }

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, Input* input, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageTimeoutW(nint window, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, nuint* result);
}
