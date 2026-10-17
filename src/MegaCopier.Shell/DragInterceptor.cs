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
/// </summary>
public sealed unsafe partial class DragInterceptor : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
    private const uint LlmhfInjected = 0x1;
    private const int VkControl = 0x11, VkShift = 0x10, VkMenu = 0x12, VkEscape = 0x1B;

    /// <summary>Marca de las pulsaciones que envía MegaCopier ("MCPY"), para que el gancho las deje pasar.</summary>
    private const nuint Marker = 0x4D435059;

    private static DragInterceptor? _active;

    private readonly Func<DragRequest, bool> _handler;
    private readonly BlockingCollection<Pending> _work = new();
    private readonly Thread _worker;
    private nint _hook;
    private nint _downView;
    private uint _downThread;

    /// <param name="handler">Recibe la copia en el hilo de trabajo; devuelve true si MegaCopier la acepta.</param>
    public DragInterceptor(Func<DragRequest, bool> handler)
    {
        _handler = handler;
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "MegaCopier.Drag" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>Se dispara (en el hilo de trabajo) cuando un arrastre se devuelve al Explorador, con el motivo.</summary>
    public event Action<string>? PassedThrough;

    public bool IsEnabled => _hook != 0;

    /// <summary>Instala el gancho. Debe llamarse desde un hilo con bucle de mensajes (el de la interfaz).</summary>
    public void Enable()
    {
        if (_hook != 0)
        {
            return;
        }

        _active = this;
        _hook = SetWindowsHookExW(WhMouseLl, &HookProc, GetModuleHandleW(null), 0);
        if (_hook == 0)
        {
            throw new InvalidOperationException($"No se pudo instalar el gancho de ratón (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public void Disable()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }

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
        if (code >= 0 && self is not null && self._hook != 0)
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
        foreach (var pending in _work.GetConsumingEnumerable())
        {
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
            if (reason is null && Stopwatch.GetElapsedTime(pending.Timestamp) > TimeSpan.FromSeconds(2))
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

    private static string? Decide(Pending pending, out DragRequest? request)
    {
        var source = ExplorerLocator.Snapshot(pending.Source, selection: true, items: false);
        DropSpot spot = UiaHitTest.At(pending.X, pending.Y, out string? itemName);
        var target = ExplorerLocator.Snapshot(pending.Target, selection: false, items: spot == DropSpot.Item);
        if (source is null || target is null)
        {
            request = null;
            return "no se encontró la ventana del Explorador";
        }

        return DragDecision.Decide(
            source.Selection, source.Folder, target.Folder, spot, itemName, target.Items,
            pending.Ctrl, pending.Shift, alt: false, out request);
    }

    /// <summary>Esc para el arrastre del Explorador; true cuando ya ha soltado el ratón (el arrastre terminó).</summary>
    private static bool CancelDrag(uint thread)
    {
        SendKey(VkEscape);
        for (int i = 0; i < 40; i++)
        {
            if (Native.ClassName(Native.CaptureWindow(thread)) != "CLIPBRDWNDCLASS")
            {
                return true;
            }

            Thread.Sleep(20);
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

    private readonly record struct Pending(nint Source, nint Target, int X, int Y, bool Ctrl, bool Shift, uint Thread, long Timestamp);

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

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int hookType, delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
