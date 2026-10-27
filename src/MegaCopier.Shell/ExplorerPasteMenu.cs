using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Intercepta Pegar en el menú contextual de una vista del Explorador. El gancho solo retiene el botón al soltar
/// sobre un emergente del mismo Explorador; UI Automation y COM se consultan en un STA aparte con plazo de devolución.
/// No modifica menús ni inyecta DLL. Conserva la vista y el punto donde se abrió el menú, también con el teclado.
/// </summary>
internal sealed unsafe partial class ExplorerPasteMenu : IDisposable
{
    private const nuint Marker = 0x4D435050; // distinto de la restauración de Ctrl del gancho de arrastre
    private static ExplorerPasteMenu? _active;
    private readonly HookThread _hooks;
    private readonly Action<PasteRequest> _handler;
    private readonly Action<string> _trace;
    private volatile Context? _context;
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

    internal void BeginKeyboardContext()
    {
        nint root = Native.GetForegroundWindow();
        nint focus = Native.DesktopFileFocus(root, Native.FocusedWindow(root));
        _context = CreateContext(focus, null, null);
        _pressedPopup = 0;
    }

    internal bool TryHandleKey(ushort key, bool pasteShortcut)
    {
        var context = _context;
        if (context is null || !ClipboardFiles.HasFiles() || Stopwatch.GetElapsedTime(context.Started) > TimeSpan.FromSeconds(60)) return false;
        if (!OwnsKeyboardForeground(context.Root)) return false;
        nint foreground = Native.GetForegroundWindow();
        nint focus = Native.FocusedWindow(foreground);
        uint thread = Native.GetWindowThreadProcessId(foreground, out _);
        if (Native.MenuOwner(thread) == 0 && Native.ClassName(focus) is not
                ("InputSiteWindowClass" or "Microsoft.UI.Content.PopupWindowSiteBridge" or "Microsoft.UI.Content.DesktopChildSiteBridge")
            && !IsDesktopMenuHost(Native.ClassName(context.Root), Native.ClassName(foreground))) return false;
        if (Interlocked.CompareExchange(ref _working, 1, 0) != 0) return true;
        var held = new HeldInput(key, ctrl: pasteShortcut && key == 0x56, shift: pasteShortcut && key == 0x2D);
        Start(context, 0, null, null, held, pasteShortcut);
        return true;
    }

    private static Context? CreateContext(nint window, int? x, int? y)
    {
        nint view = Native.FindAncestorOfClass(window, "SHELLDLL_DefView");
        bool tree = view == 0 && Native.ClassName(window) == "SysTreeView32"
            && Native.FindAncestorOfClass(window, "NamespaceTreeControl") != 0;
        if (tree) view = window;
        if (view == 0 && x is null && Native.ClassName(window) == "CabinetWClass") view = window;
        if (view == 0 || !ExplorerLocator.IsExplorerOrDesktop(view)) return null;
        nint root = Native.GetAncestor(view, 2);
        if (!Native.GetWindowRect(root, out var bounds)) return null;
        return new Context(view, root, Stopwatch.GetTimestamp(), x, y, tree, bounds);
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
                if (mouse->Extra != Marker && mouse->Extra != 0x4D435059 && self.OnMouse((uint)wParam, mouse->X, mouse->Y))
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
            _context = CreateContext(window, x, y);
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
        Start(context, popup, x, y, new HeldInput(), pasteShortcut: false);
        return true;
    }

    private void Start(Context context, nint popup, int? x, int? y, HeldInput click, bool pasteShortcut)
    {
        var thread = new Thread(() => HandleClick(context, popup, x, y, click, pasteShortcut))
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

    }

    private void HandleClick(Context context, nint popup, int? x, int? y, HeldInput click, bool pasteShortcut)
    {
        try
        {
            bool keyboard = x is null && y is null;
            if (!_hooks.IsInstalled || (keyboard && !UiaHitTest.IsContextMenuFocused())
                || (!pasteShortcut && !(x is { } px && y is { } py
                    ? UiaHitTest.IsPasteMenuAt(px, py, context.Root) : UiaHitTest.IsPasteMenuFocused(context.Root))))
            {
                return;
            }

            string? destination = Destination(context);
            string? reason = PasteDecision.Decide(ClipboardFiles.TryRead(), destination, out var request);
            if (reason is not null)
            {
                _trace($"Pegar del menú devuelto al Explorador: {reason}");
                return;
            }

            // Si cambió la aplicación activa o venció el plazo, no se cierra ningún menú ni se copia tarde.
            if (!_hooks.IsInstalled || !ReferenceEquals(_context, context)
                || !(keyboard ? OwnsKeyboardForeground(context.Root) : OwnsForeground(context.Root, popup))
                || (!keyboard && !IsWindowVisible(popup)) || !click.Claim())
            {
                _trace("Pegar del menú devuelto al Explorador: cambió el menú, el foco o venció el plazo");
                return;
            }

            if (!CloseMenu(popup, context.Root))
            {
                _trace("Pegar del menú devuelto al Explorador: no se confirmó el cierre del menú");
                return;
            }

            click.Accept();
            _context = null;
            if (request is not null) _handler(request);
            else _trace("Pegar del menú atendido: los archivos cortados ya están en su destino");
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

    private static string? Destination(Context context)
    {
        if (!Native.GetWindowRect(context.Root, out var bounds) || !bounds.SameAs(context.Bounds)) return null;
        if (context.Tree)
        {
            var names = context.X is { } tx && context.Y is { } ty
                ? UiaHitTest.TreePathInWindow(context.View, tx, ty) : UiaHitTest.SelectedTreePath(context.View);
            return names is null ? null : ShellNamespace.Resolve(names);
        }
        if (context.X is { } x && context.Y is { } y)
        {
            DropSpot spot = UiaHitTest.InFileView(context.View, x, y, out var name);
            return PasteDecision.ContextDestination(ExplorerLocator.Snapshot(context.View, selection: false, items: spot == DropSpot.Item), spot, name);
        }
        nint fileView = Native.ClassName(context.View) == "CabinetWClass" ? UiaHitTest.ActiveFileView(context.View) : context.View;
        var snapshot = fileView == 0 ? null : ExplorerLocator.Snapshot(fileView, selection: true, items: false);
        return snapshot?.Selection switch
        {
            { Count: 0 } => snapshot.Folder,
            { Count: 1 } selected when Directory.Exists(selected[0]) => selected[0],
            _ => null,
        };
    }

    /// <summary>El Escritorio moderno usa un host XAML independiente de Progman/WorkerW, dentro del mismo Explorador.</summary>
    internal static bool IsDesktopMenuHost(string sourceClass, string hostClass) =>
        sourceClass is "Progman" or "WorkerW" && hostClass.StartsWith("XamlExplorerHostIslandWindow", StringComparison.Ordinal);

    internal static bool OwnsForeground(nint source, nint popup)
    {
        nint foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(source, out uint sourcePid);
        Native.GetWindowThreadProcessId(foreground, out uint foregroundPid);
        Native.GetWindowThreadProcessId(popup, out uint popupPid);
        if (sourcePid == 0 || foregroundPid != sourcePid || popupPid != sourcePid) return false;
        nint host = Native.GetAncestor(popup, 3);
        if (Native.ClassName(popup) == "#32768")
        {
            nint owner = Native.MenuOwner(Native.GetWindowThreadProcessId(popup, out _));
            if (owner != 0) host = Native.GetAncestor(owner, 2);
        }
        if (host == source) return foreground == source || Native.GetAncestor(foreground, 3) == source;
        return IsDesktopMenuHost(Native.ClassName(source), Native.ClassName(host))
            && (foreground == host || Native.GetAncestor(foreground, 3) == host);
    }

    private static bool OwnsKeyboardForeground(nint source)
    {
        nint foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(source, out uint sourcePid);
        Native.GetWindowThreadProcessId(foreground, out uint foregroundPid);
        return sourcePid != 0 && sourcePid == foregroundPid
            && (foreground == source || Native.GetAncestor(foreground, 3) == source
                || IsDesktopMenuHost(Native.ClassName(source), Native.ClassName(foreground)));
    }

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
        if (popup == 0)
        {
            // Con teclado, Windows 11 puede dibujar el menú dentro de la pestaña. No hay un HWND que ocultar.
            // Se comprueba el foco UIA, sin confundir con los puentes vacíos que Windows mantiene visibles.
            uint thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
            nint menuOwner = Native.MenuOwner(thread);
            if (menuOwner != 0)
            {
                nuint result = 0;
                SendMessageTimeoutW(menuOwner, 0x001F, 0, 0, 0x0003, 250, &result);
            }
            else
            {
                for (int i = 0; i < 50 && (Native.GetAsyncKeyState(0x11) & 0x8000) != 0; i++) Thread.Sleep(10);
                if ((Native.GetAsyncKeyState(0x11) & 0x8000) != 0 || !Key(up: false)) return false;
                Key(up: true);
            }
            for (int i = 0; i < 30; i++)
            {
                if (!UiaHitTest.IsContextMenuFocused()) return true;
                Thread.Sleep(10);
            }
            return false;
        }
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

        // Con Ctrl+V en un menú moderno se espera a soltar Ctrl: enviar Ctrl+Esc abriría Inicio.
        for (int i = 0; i < 50 && (Native.GetAsyncKeyState(0x11) & 0x8000) != 0; i++) Thread.Sleep(10);
        if ((Native.GetAsyncKeyState(0x11) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0 || !Key(up: false))
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

    private sealed class HeldInput(ushort key = 0, bool ctrl = false, bool shift = false) : IDisposable
    {
        private int _claimed, _released;
        private bool _accepted;
        private readonly nint _foreground = key == 0 ? 0 : Native.GetForegroundWindow();
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

        public void Accept()
        {
            _accepted = true;
            Release();
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                if (key == 0)
                {
                    var input = new Input { Type = 0, Mouse = new MouseInput { Flags = 0x0004, Extra = Marker } };
                    SendInput(1, &input, sizeof(Input));
                }
                else if (!_accepted && Native.GetForegroundWindow() == _foreground)
                {
                    bool addCtrl = ctrl && (Native.GetAsyncKeyState(0x11) & 0x8000) == 0;
                    bool addShift = shift && (Native.GetAsyncKeyState(0x10) & 0x8000) == 0;
                    if (addCtrl) Key(up: false, 0x11);
                    if (addShift) Key(up: false, 0x10);
                    try { Key(up: false, key); Key(up: true, key); }
                    finally
                    {
                        if (addShift) Key(up: true, 0x10);
                        if (addCtrl) Key(up: true, 0x11);
                    }
                }
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _claimed, 1);
            _timer?.Dispose();
            Release();
        }
    }

    private static bool Key(bool up, ushort key = 0x1B)
    {
        var input = new Input { Type = 1, Key = new KeyboardInput { VirtualKey = key, Flags = up ? 2u : 0u, Extra = Marker } };
        return SendInput(1, &input, sizeof(Input)) == 1;
    }

    private sealed record Context(nint View, nint Root, long Started, int? X, int? Y, bool Tree, Native.WindowRect Bounds);

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
