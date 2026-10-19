using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Sustituye el arrastre con el botón izquierdo entre ventanas del Explorador por MegaCopier, sin inyectar nada en
/// el Explorador.
///
/// Un gancho de ratón de bajo nivel recuerda en qué lista de archivos se pulsó el botón. Al soltarlo comprueba, con
/// llamadas instantáneas, que hay un arrastre OLE en curso desde ese hilo del Explorador (durante DoDragDrop el ratón
/// lo captura una ventana CLIPBRDWNDCLASS; una selección con recuadro no) y que se suelta sobre otra lista de archivos.
/// Entonces retiene ese "soltar" y en un hilo aparte decide (qué hay seleccionado, qué hay bajo el puntero, qué teclas
/// hay pulsadas) con <see cref="DragDecision"/>:
///  - si es para MegaCopier, cancela el arrastre del Explorador con Esc, devuelve el botón a su estado y copia;
///  - si no, suelta el botón como si nada: el Explorador hace lo de siempre.
/// Si el Explorador no cancela a tiempo, también se le devuelve: nunca copian los dos.
///
/// Para decidir deprisa, lo que no cambia durante el arrastre (qué se arrastra) se lee en cuanto empieza, mientras el
/// usuario aún mueve el ratón; al soltar solo falta mirar el destino. Así casi nunca se agota el tiempo de decidir
/// (que haría que copiara el Explorador en lugar de MegaCopier).
/// </summary>
public sealed unsafe partial class DragInterceptor : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
    private const uint LlmhfInjected = 0x1;
    private const int VkControl = 0x11, VkShift = 0x10, VkMenu = 0x12, VkEscape = 0x1B;

    /// <summary>Marca de las pulsaciones que envía MegaCopier ("MCPY"), para que el gancho las deje pasar.</summary>
    private const nuint Marker = 0x4D435059;

    private static DragInterceptor? _active;

    private readonly Func<DragRequest, bool> _handler;
    private readonly BlockingCollection<object> _work = new();
    private readonly Thread _worker;
    private readonly HookThread _hooks;
    private nint _downView;
    private uint _downThread;
    private bool _prefetchQueued;

    /// <summary>Lo arrastrado, leído al empezar el arrastre (solo lo usa el hilo de trabajo).</summary>
    private (nint View, ViewSnapshot? Snapshot) _prefetched;

    /// <param name="handler">Recibe la copia en el hilo de trabajo; devuelve true si MegaCopier la acepta.</param>
    public DragInterceptor(Func<DragRequest, bool> handler)
    {
        _handler = handler;
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "MegaCopier.Drag" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
        _hooks = new HookThread(WhMouseLl, &HookProc, "MegaCopier.MouseHook");
    }

    /// <summary>Se dispara (en el hilo de trabajo) cuando un arrastre se devuelve al Explorador, con el motivo.</summary>
    public event Action<string>? PassedThrough;

    public bool IsEnabled => _hooks.IsInstalled;

    /// <summary>Instala el gancho, en un hilo propio (ver <see cref="HookThread"/>).</summary>
    public void Enable()
    {
        _active = this;
        _hooks.Start();
    }

    public void Disable()
    {
        _hooks.Stop();
        _downView = 0;
    }

    public void Dispose()
    {
        Disable();
        _work.CompleteAdding();
    }

    private static bool Pressed(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>La lista de archivos (vista del Explorador o del Escritorio) que hay en esa ventana, o 0.</summary>
    private static nint FileView(nint window) =>
        window != 0 && ExplorerLocator.IsExplorerOrDesktop(window) && Native.FindAncestorOfClass(window, "SHELLDLL_DefView") != 0 ? window : 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        var self = _active;
        if (code >= 0 && self is not null && self.IsEnabled)
        {
            try
            {
                var data = (MsllHookStruct*)lParam;
                if ((data->flags & LlmhfInjected) == 0 && self.OnButton((uint)wParam, data->x, data->y))
                {
                    return 1; // el Explorador no ve este "soltar" (todavía)
                }
            }
            catch
            {
                // Una excepción no puede cruzar a código nativo; ante la duda, que el ratón funcione como siempre.
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>Dentro del gancho: solo comprobaciones instantáneas. True para retener el mensaje.</summary>
    private bool OnButton(uint message, int x, int y)
    {
        if (message == WmLButtonDown)
        {
            _downView = FileView(Native.WindowFromPoint(new Native.Point(x, y)));
            _downThread = _downView == 0 ? 0 : Native.GetWindowThreadProcessId(_downView, out _);
            _prefetchQueued = false;
            return false;
        }

        // Empieza un arrastre desde una lista de archivos: lo arrastrado se lee ya, sin esperar a que se suelte.
        if (message == WmMouseMove)
        {
            if (_downView != 0 && !_prefetchQueued && Native.ClassName(Native.CaptureWindow(_downThread)) == "CLIPBRDWNDCLASS")
            {
                _prefetchQueued = _work.TryAdd(new Prefetch(_downView));
            }

            return false;
        }

        if (message != WmLButtonUp || _downView == 0)
        {
            return false;
        }

        nint source = _downView;
        uint thread = _downThread;
        _downView = 0;

        // ¿Arrastre OLE en curso desde ese hilo del Explorador? (una selección con recuadro captura otra ventana)
        if (Native.ClassName(Native.CaptureWindow(thread)) != "CLIPBRDWNDCLASS" || Pressed(VkMenu))
        {
            return false;
        }

        nint target = FileView(Native.WindowFromPoint(new Native.Point(x, y)));
        if (target == 0)
        {
            return false; // panel de navegación, barra de direcciones, otro programa...: lo suelta el Explorador
        }

        return _work.TryAdd(new Pending(source, target, x, y, Pressed(VkControl), Pressed(VkShift), thread, Stopwatch.GetTimestamp()));
    }

    private void WorkLoop()
    {
        foreach (var item in _work.GetConsumingEnumerable())
        {
            if (item is Prefetch prefetch)
            {
                try
                {
                    _prefetched = (prefetch.Source, ExplorerLocator.Snapshot(prefetch.Source, selection: true, items: false));
                    UiaHitTest.WarmUp(); // la primera consulta de accesibilidad es la más lenta: se hace ya
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException)
                {
                    _prefetched = default;
                }

                continue;
            }

            var pending = (Pending)item;
            string? reason;
            DragRequest? request = null;
            try
            {
                reason = Decide(pending, out request);
            }
            catch (Exception ex)
            {
                reason = ex.Message;
            }

            // Si decidir tardó demasiado, el usuario puede haber seguido moviendo el ratón: mejor que suelte el Explorador.
            if (reason is null && Stopwatch.GetElapsedTime(pending.Timestamp) > TimeSpan.FromSeconds(4))
            {
                reason = "se tardó demasiado en decidir";
            }

            if (reason is null && !CancelDrag(pending.Thread))
            {
                reason = "el Explorador no canceló su arrastre a tiempo";
            }

            if (reason is not null)
            {
                SendButtonUp(); // el Explorador suelta como siempre
                PassedThrough?.Invoke(reason);
                continue;
            }

            SendButtonUp(); // el arrastre ya está cancelado: solo devuelve el botón a su estado real
            try
            {
                if (!_handler(request!))
                {
                    PassedThrough?.Invoke("MegaCopier no aceptó la copia");
                }
            }
            catch (Exception ex)
            {
                PassedThrough?.Invoke(ex.Message);
            }
        }
    }

    private string? Decide(Pending pending, out DragRequest? request)
    {
        var source = _prefetched.View == pending.Source && _prefetched.Snapshot is { } ready
            ? ready
            : ExplorerLocator.Snapshot(pending.Source, selection: true, items: false);
        _prefetched = default;
        DropSpot spot = UiaHitTest.At(pending.X, pending.Y, out string? itemName);
        var target = ExplorerLocator.Snapshot(pending.Target, selection: false, items: false);
        if (source is null || target is null)
        {
            request = null;
            return "no se encontró la ventana del Explorador";
        }

        // Sobre un elemento: si es una carpeta con ese nombre, no hace falta leer toda la carpeta de destino (en
        // carpetas grandes eso es lo que más tarda). Si no, sí: puede ser un archivo con la extensión oculta.
        IReadOnlyList<ViewItem> items = [];
        if (spot == DropSpot.Item && itemName is not null && target.Folder is not null)
        {
            string folder = Path.Combine(target.Folder, itemName);
            items = itemName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Directory.Exists(folder)
                ? [new ViewItem(itemName, folder, IsFolder: true)]
                : ExplorerLocator.Snapshot(pending.Target, selection: false, items: true)?.Items ?? [];
        }

        return DragDecision.Decide(
            source.Selection, source.Folder, target.Folder, spot, itemName, items,
            pending.Ctrl, pending.Shift, alt: false, out request);
    }

    /// <summary>Esc para el arrastre del Explorador; true cuando ya ha soltado el ratón (el arrastre terminó).</summary>
    private static bool CancelDrag(uint thread)
    {
        // Solo cuenta como cancelado si el Explorador ha soltado el ratón y sigue sin él un momento: un cambio de
        // captura pasajero no basta (si se le devolviera el botón con el arrastre vivo, copiarían los dos).
        SendKey(VkEscape);
        int released = 0;
        for (int i = 0; i < 60; i++)
        {
            released = Native.ClassName(Native.CaptureWindow(thread)) != "CLIPBRDWNDCLASS" ? released + 1 : 0;
            if (released >= 3)
            {
                return true;
            }

            if (i == 25)
            {
                SendKey(VkEscape); // por si el primero llegó a otra ventana
            }

            Thread.Sleep(15);
        }

        return false;
    }

    private static void SendKey(int key)
    {
        var inputs = stackalloc Input[2];
        inputs[0] = new Input { type = 1 /* INPUT_KEYBOARD */ };
        inputs[0].u.ki = new KeybdInput { wVk = (ushort)key, dwExtraInfo = Marker };
        inputs[1] = inputs[0];
        inputs[1].u.ki.dwFlags = 0x2; // KEYEVENTF_KEYUP
        SendInput(2, inputs, sizeof(Input));
    }

    private static void SendButtonUp()
    {
        var input = new Input { type = 0 /* INPUT_MOUSE */ };
        input.u.mi = new MouseInput { dwFlags = 0x0004 /* MOUSEEVENTF_LEFTUP */, dwExtraInfo = Marker };
        SendInput(1, &input, sizeof(Input));
    }

    private sealed record Prefetch(nint Source);

    private sealed record Pending(nint Source, nint Target, int X, int Y, bool Ctrl, bool Shift, uint Thread, long Timestamp);

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public int x;
        public int y;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput mi;

        [FieldOffset(0)]
        public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, Input* inputs, int size);
}
