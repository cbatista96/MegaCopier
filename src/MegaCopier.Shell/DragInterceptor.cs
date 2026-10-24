using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

//// <summary>
/// Sustituye el arrastre con el botón izquierdo entre ventanas del Explorador por MegaCopier, sin inyectar nada en
/// el Explorador.
///
/// Un gancho de ratón de bajo nivel recuerda en qué lista de archivos (o en qué panel de navegación) se pulsó el
/// botón. Al soltarlo comprueba, con llamadas instantáneas, que hay un arrastre OLE en curso desde ese hilo del
/// Explorador (durante DoDragDrop el ratón lo captura una ventana CLIPBRDWNDCLASS; una selección con recuadro no) y que
/// se suelta sobre otra lista de archivos o sobre el panel de navegación. Entonces retiene ese "soltar" y, en un hilo
/// propio de ese arrastre, decide (qué hay seleccionado, qué hay bajo el puntero, qué teclas hay pulsadas) con
/// <see cref="DragDecision"/>:
///  - si es para MegaCopier, cancela el arrastre del Explorador con Esc, devuelve el botón a su estado y copia;
///  - si no, suelta el botón como si nada: el Explorador hace lo de siempre.
/// Si el Explorador no cancela a tiempo, también se le devuelve: nunca copian los dos.
///
/// Mientras dura el arrastre, el hilo del Explorador que lo lleva solo atiende a otros programas (la accesibilidad,
/// las consultas COM) cuando le llega algo del ratón o del teclado. Con el botón retenido y el ratón quieto no le llega
/// nada, y cualquier pregunta sobre esa ventana se quedaría esperando hasta el siguiente clic. Por eso, mientras se
/// decide, se le envían movimientos de ratón de cero píxeles (el puntero no se mueve) y hay un plazo: si se agota,
/// suelta el Explorador. Así el botón nunca se queda "pegado".
///
/// Para decidir deprisa, lo que no cambia durante el arrastre (qué se arrastra) se lee en cuanto empieza, mientras el
/// usuario aún mueve el ratón; al soltar solo falta mirar el destino.
/// </summary>
public sealed unsafe partial class DragInterceptor : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
    private const int VkControl = 0x11, VkShift = 0x10, VkMenu = 0x12, VkEscape = 0x1B;

    /// <summary>Lo más que se retiene el botón mientras se decide; después suelta el Explorador.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(1800);

    /// <summary>Cada cuánto se "despierta" al Explorador mientras se decide.</summary>
    private const int NudgeMilliseconds = 25;

    /// <summary>Marca de las pulsaciones que envía MegaCopier ("MCPY"), para que el gancho las deje pasar.</summary>
    private const nuint Marker = 0x4D435059;

    private static DragInterceptor? _active;

    private readonly Func<DragRequest, bool> _handler;
    private readonly HookThread _hooks;
    private readonly HookThread _controlHooks;
    private readonly DragControlState _controls = new();
    private readonly Lock _cancelLock = new();
    private nint _downView;
    private bool _downTree;
    private int _downX, _downY;
    private uint _downThread;
    private bool _prefetchQueued;
    private DragOriginProbe? _originProbe;

    /// <summary>Lo arrastrado, que se empieza a leer (en un hilo propio) en cuanto empieza el arrastre.</summary>
    private volatile Prefetch? _prefetch;

    /// <param name="handler">Recibe la copia en un hilo aparte; devuelve true si MegaCopier la acepta.</param>
    public DragInterceptor(Func<DragRequest, bool> handler)
    {
        _handler = handler;
        _hooks = new HookThread(WhMouseLl, &HookProc, "MegaCopier.MouseHook");
        _controlHooks = new HookThread(13 /* WH_KEYBOARD_LL */, &ControlHookProc, "MegaCopier.DragControlHook");
        _controlHooks.Reinstalled += _ => _controls.Reset(Pressed(0xA2), Pressed(0xA3));
        _hooks.Reinstalled += result => Traced?.Invoke($"Windows había quitado el gancho del ratón: {result}");
    }

    /// <summary>Se dispara (en otro hilo) cuando un arrastre se devuelve al Explorador, con el motivo.</summary>
    public event Action<string>? PassedThrough;

    /// <summary>Se dispara (en otro hilo) con cada arrastre que se queda MegaCopier o que se decide tarde.</summary>
    public event Action<string>? Traced;

    public bool IsEnabled => _hooks.IsInstalled;

    /// <summary>Instala el gancho, en un hilo propio (ver <see cref="HookThread"/>).</summary>
    public void Enable()
    {
        _active = this;
        _controls.Reset(Pressed(0xA2), Pressed(0xA3));
        _controlHooks.Start();
        try { _hooks.Start(); }
        catch { _controlHooks.Stop(); throw; }
        ShellNamespace.WarmUpInBackground(); // por si se suelta en el panel de navegación
    }

    public void Disable()
    {
        _hooks.Stop();
        lock (_cancelLock) _controlHooks.Stop();
        _downView = 0;
    }

    public void Dispose() => Disable();

    private static bool Pressed(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>La lista de archivos (vista del Explorador o del Escritorio) que hay en esa ventana, o 0.</summary>
    private static nint FileView(nint window) =>
        window != 0 && ExplorerLocator.IsExplorerOrDesktop(window) && Native.FindAncestorOfClass(window, "SHELLDLL_DefView") != 0 ? window : 0;

    /// <summary>El panel de navegación (el árbol de la izquierda) de una ventana del Explorador, o 0.</summary>
    private static nint NavigationTree(nint window) =>
        window != 0 && Native.ClassName(window) == "SysTreeView32" && ExplorerLocator.IsExplorerOrDesktop(window)
            && Native.FindAncestorOfClass(window, "NamespaceTreeControl") != 0 ? window : 0;

    /// <summary>Dónde empieza o termina un arrastre que MegaCopier puede quedarse: una lista de archivos o el panel.</summary>
    private static (nint Window, bool Tree) DragSpot(nint window) =>
        FileView(window) is not 0 and var view ? (view, false) : (NavigationTree(window), true);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        var self = _active;
        self?._hooks.Touch();
        if (code >= 0 && self is not null && self.IsEnabled)
        {
            try
            {
                var data = (MsllHookStruct*)lParam;
                // También funcionan ratones remotos y herramientas de accesibilidad. Solo se ignoran nuestros
                // propios mensajes (soltar, Esc y movimientos de mantenimiento), para no interceptarnos otra vez.
                if (data->dwExtraInfo != Marker && self.OnButton((uint)wParam, data->x, data->y))
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
            (_downView, _downTree) = DragSpot(Native.WindowFromPoint(new Native.Point(x, y)));
            _downX = x;
            _downY = y;
            _downThread = _downView == 0 ? 0 : Native.GetWindowThreadProcessId(_downView, out _);
            _prefetchQueued = false;
            _prefetch = null;
            _originProbe = _downView != 0 && !_downTree ? DragOriginProbe.Start(x, y) : null;
            return false;
        }

        // Empieza un arrastre: lo arrastrado se lee ya, mientras el ratón se mueve, sin esperar a que se suelte.
        if (message == WmMouseMove)
        {
            if (_downView != 0 && !_prefetchQueued && Native.ClassName(Native.CaptureWindow(_downThread)) == "CLIPBRDWNDCLASS")
            {
                _prefetchQueued = true;
                _prefetch = Prefetch.Start(_downView, _downTree, _downX, _downY);
            }

            return false;
        }

        if (message != WmLButtonUp || _downView == 0)
        {
            return false;
        }

        nint source = _downView;
        bool sourceTree = _downTree;
        uint thread = _downThread;
        _downView = 0;

        // ¿Arrastre OLE en curso desde ese hilo del Explorador? (una selección con recuadro captura otra ventana)
        string captureClass = Native.ClassName(Native.CaptureWindow(thread));
        bool awaitDrag = captureClass != "CLIPBRDWNDCLASS";
        bool movedFar = Math.Abs(x - _downX) >= GetSystemMetrics(68 /* SM_CXDRAG */)
            || Math.Abs(y - _downY) >= GetSystemMetrics(69 /* SM_CYDRAG */);
        DropSpot origin = sourceTree ? DropSpot.Item : _originProbe?.Spot ?? DropSpot.Unknown;
        _originProbe = null;
        // Un recuadro empieza en el fondo: su "soltar" pasa inmediatamente, sin temporizador,
        // consultas COM ni pulsaciones sintéticas. La espera solo sirve para un arrastre rápido
        // que empezó sobre un elemento y cuyo bucle OLE aún no ha arrancado.
        if (Pressed(VkMenu) || (awaitDrag && !DragOriginProbe.CanAwaitOle(captureClass, origin, movedFar)))
        {
            return false;
        }

        var (target, tree) = DragSpot(Native.WindowFromPoint(new Native.Point(x, y)));
        if (target == 0)
        {
            return false; // barra de direcciones, otro programa...: lo suelta el Explorador
        }

        var pending = new Pending(source, sourceTree, target, tree, x, y, Pressed(VkControl), Pressed(VkShift), thread, awaitDrag);
        new Drop(this, pending, _prefetch).Start();
        _prefetch = null;
        return true;
    }

    /// <summary>
    /// Lo que se arrastra, leído en un hilo propio en cuanto empieza el arrastre (mientras el usuario mueve el ratón,
    /// el Explorador responde enseguida).
    /// </summary>
    private sealed class Prefetch
    {
        private readonly TaskCompletionSource<ViewSnapshot?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Prefetch(nint source)
        {
            Source = source;
        }

        public nint Source { get; }

        public Task<ViewSnapshot?> Snapshot => _done.Task;

        public static Prefetch Start(nint source, bool tree, int x, int y)
        {
            var prefetch = new Prefetch(source);
            var thread = new Thread(() =>
            {
                try
                {
                    prefetch._done.TrySetResult(tree ? TreeSource(x, y) : ExplorerLocator.Snapshot(source, selection: true, items: false));
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    prefetch._done.TrySetResult(null);
                }
                finally
                {
                    UiaHitTest.ReleaseThread();
                }
            })
            { IsBackground = true, Name = "MegaCopier.DragSource" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return prefetch;
        }
    }

    /// <summary>
    /// Un "soltar" retenido: se decide en un hilo propio mientras un temporizador mantiene despierto al Explorador y
    /// vigila el plazo. Quien llega antes (la decisión o el plazo) se queda el arrastre; el otro ya no hace nada.
    /// </summary>
    private sealed class Drop(DragInterceptor owner, Pending pending, Prefetch? prefetch)
    {
        private const int Deciding = 0, Returned = 1, Taken = 2;

        private readonly long _started = Stopwatch.GetTimestamp();
        private int _state;
        private Timer? _timer;

        /// <summary>Por dónde va la decisión (para el registro, si se agota el plazo).</summary>
        public volatile string Stage = "empezando";

        public Pending Pending { get; } = pending;

        public Prefetch? Prefetch { get; } = prefetch;

        public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);

        public TimeSpan Remaining => Deadline - Elapsed;

        public void Start()
        {
            _timer = new Timer(_ => Tick(), null, NudgeMilliseconds, NudgeMilliseconds);
            var thread = new Thread(() => owner.Run(this)) { IsBackground = true, Name = "MegaCopier.Drop" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void Tick()
        {
            if (Volatile.Read(ref _state) != Deciding)
            {
                _timer?.Dispose();
                return;
            }

            if (Elapsed < Deadline)
            {
                Nudge(Pending.Thread);
                return;
            }

            if (Interlocked.CompareExchange(ref _state, Returned, Deciding) == Deciding)
            {
                _timer?.Dispose();
                SendButtonUp(); // se agotó el plazo: suelta el Explorador
                owner.PassedThrough?.Invoke($"se agotó el tiempo de decidir ({Stage}, {Elapsed.TotalMilliseconds:0} ms)");
            }
        }

        /// <summary>True si la decisión llega a tiempo (entonces el plazo ya no la toca).</summary>
        public bool Claim()
        {
            bool claimed = Interlocked.CompareExchange(ref _state, Taken, Deciding) == Deciding;
            _timer?.Dispose();
            return claimed;
        }
    }

    private void Run(Drop drop)
    {
        string? reason;
        DragRequest? request = null;
        try
        {
            reason = Decide(drop, out request);
        }
        catch (Exception ex)
        {
            reason = ex.Message;
        }
        finally
        {
            UiaHitTest.ReleaseThread();
        }

        string took = $"{drop.Elapsed.TotalMilliseconds:0} ms";
        if (!drop.Claim())
        {
            Traced?.Invoke($"decidido tarde ({took}): {reason ?? "era para MegaCopier"}; ya lo había soltado el Explorador");
            return;
        }

        if (reason is null && !CancelDrag(drop.Pending.Thread))
        {
            reason = "el Explorador no canceló su arrastre a tiempo";
        }

        took = $"{drop.Elapsed.TotalMilliseconds:0} ms"; // incluye la confirmación de cancelación, no solo la decisión
        SendButtonUp(); // el Explorador suelta como siempre o, si ya se canceló, solo vuelve el botón a su estado real
        if (reason is not null)
        {
            PassedThrough?.Invoke($"{reason} ({took})");
            return;
        }

        Traced?.Invoke($"{(request!.Move ? "mover" : "copiar")} {request.Sources.Count} elemento(s) a {request.Destination} ({took})");
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

    private static string? Decide(Drop drop, out DragRequest? request)
    {
        request = null;
        var pending = drop.Pending;

        // El gancho ve «soltar» antes que el Explorador. Con gestos rápidos, este aún está procesando los
        // movimientos que inician DoDragDrop y conserva la captura en la lista. Se le deja atenderlos, sin
        // entregarle todavía «soltar». Un recuadro de selección nunca pasa a captura OLE: se devuelve intacto.
        if (pending.AwaitDrag)
        {
            drop.Stage = "esperando el inicio del arrastre";
            long started = Stopwatch.GetTimestamp();
            while (Native.ClassName(Native.CaptureWindow(pending.Thread)) != "CLIPBRDWNDCLASS")
            {
                if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(250))
                {
                    return "el gesto no inició un arrastre OLE";
                }

                Thread.Sleep(10);
            }
        }

        // Lo arrastrado: casi siempre ya está leído. Si no, se espera lo que quede de plazo (o se lee ahora).
        drop.Stage = "leyendo lo que se arrastra";
        ViewSnapshot? source;
        if (drop.Prefetch is { } prefetch && prefetch.Source == pending.Source)
        {
            source = prefetch.Snapshot.Wait(drop.Remaining > TimeSpan.Zero ? drop.Remaining : TimeSpan.Zero) ? prefetch.Snapshot.Result : null;
        }
        else
        {
            source = pending.SourceTree ? null : ExplorerLocator.Snapshot(pending.Source, selection: true, items: false);
        }

        if (source is null)
        {
            return "no se pudo leer qué se arrastra";
        }

        // Sobre el panel de navegación: la carpeta (o unidad) del elemento bajo el puntero.
        if (pending.TargetTree)
        {
            drop.Stage = "mirando el panel de navegación";
            var names = UiaHitTest.TreePathAt(pending.X, pending.Y);
            if (names is null)
            {
                return "no hay una carpeta del panel bajo el puntero";
            }

            drop.Stage = $"buscando «{string.Join(" › ", names)}»";
            string? folder = ShellNamespace.Resolve(names);
            if (folder is null)
            {
                return $"«{string.Join(" › ", names)}» no es una carpeta del disco";
            }

            return DragDecision.Decide(
                source.Selection, source.Folder, folder, DropSpot.Folder, null, [],
                pending.Ctrl, pending.Shift, alt: false, out request);
        }

        drop.Stage = "mirando qué hay bajo el puntero";
        DropSpot spot = UiaHitTest.At(pending.X, pending.Y, out string? itemName);
        drop.Stage = "leyendo la carpeta de destino";
        var target = ExplorerLocator.Snapshot(pending.Target, selection: false, items: false);
        if (target is null)
        {
            return "no se encontró la ventana del Explorador";
        }

        // Sobre un elemento: si es una carpeta con ese nombre, no hace falta leer toda la carpeta de destino (en
        // carpetas grandes eso es lo que más tarda). Si no, sí: puede ser un archivo con la extensión oculta, o una
        // unidad de «Este equipo» (vista virtual, sin carpeta).
        IReadOnlyList<ViewItem> items = [];
        if (spot == DropSpot.Item && itemName is not null)
        {
            string? folder = target.Folder is null ? null : Path.Combine(target.Folder, itemName);
            drop.Stage = "leyendo los elementos del destino";
            items = folder is not null && itemName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Directory.Exists(folder)
                ? [new ViewItem(itemName, folder, IsFolder: true)]
                : ExplorerLocator.Snapshot(pending.Target, selection: false, items: true)?.Items ?? [];
        }

        drop.Stage = "decidiendo";
        return DragDecision.Decide(
            source.Selection, source.Folder, target.Folder, spot, itemName, items,
            pending.Ctrl, pending.Shift, alt: false, out request);
    }
    /// <summary>
    /// Lo que se arrastra desde el panel de navegación: la carpeta del elemento donde se pulsó (se lee al empezar a
    /// arrastrar, antes de que el panel se desplace o despliegue nada). Null si no es una carpeta del disco.
    /// </summary>
    private static ViewSnapshot? TreeSource(int x, int y)
    {
        if (UiaHitTest.TreePathAt(x, y) is not { } names || ShellNamespace.Resolve(names) is not { } path)
        {
            return null;
        }

        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        return parent is null ? null : new ViewSnapshot(parent, [path], []); // una unidad entera: lo hace el Explorador
    }

    /// <summary>Esc para el arrastre del Explorador; true cuando ya ha soltado el ratón (el arrastre terminó).</summary>
    private bool CancelDrag(uint thread)
    {
        lock (_cancelLock)
        {
            if (Pressed(VkEscape) || !_controlHooks.IsInstalled) return false;
            _controls.Suspended = true;
            bool escapeDown = false;
            try
            {
                // Ctrl+Esc abre Inicio. Soltar Ctrl lógicamente antes de enviar Esc;
                // el gancho conserva su estado físico sin generar ese atajo global.
                if (!SendKey(0xA2, up: true) || !SendKey(0xA3, up: true)) return false;
                escapeDown = SendKey(VkEscape, up: false);
                return escapeDown && WaitForDragCancellation(thread);
            }
            finally
            {
                if (escapeDown) SendKey(VkEscape, up: true);
                _controls.Suspended = false;
                // El gancho descarta estas restauraciones si el usuario ya soltó Ctrl.
                SendKey(0xA2, up: false);
                SendKey(0xA3, up: false);
            }
        }
    }

    private static bool WaitForDragCancellation(uint thread)
    {
        // Solo cuenta como cancelado si el Explorador ha soltado el ratón y sigue sin él un momento: un cambio de
        // captura pasajero no basta (si se le devolviera el botón con el arrastre vivo, copiarían los dos).
        // DoDragDrop consulta Esc al atender el siguiente cambio de entrada. No se debe soltar en el mismo
        // lote: entre ventanas (o con otro trabajo activo) el bucle OLE puede atenderlo después del KEYUP.
        // Se mantiene pulsada hasta confirmar la cancelación y se despierta también el hilo de origen.
        int released = 0;
        for (int i = 0; i < 60; i++)
        {
            released = Native.ClassName(Native.CaptureWindow(thread)) != "CLIPBRDWNDCLASS" ? released + 1 : 0;
            if (released >= 3)
            {
                return true;
            }

            Nudge(thread);
            Thread.Sleep(15);
        }

        return false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint ControlHookProc(int code, nint wParam, nint lParam)
    {
        var self = _active;
        self?._controlHooks.Touch();
        if (code >= 0 && self is not null)
        {
            var data = (ControlHookData*)lParam;
            if (self._controls.Block((int)data->Key, (data->Flags & 0x80) != 0, data->ExtraInfo == Marker)) return 1;
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ControlHookData
    {
        public uint Key, ScanCode, Flags, Time;
        public nuint ExtraInfo;
    }

    private static bool SendKey(int key, bool up)
    {
        var input = new Input { type = 1 /* INPUT_KEYBOARD */ };
        input.u.ki = new KeybdInput { wVk = (ushort)key, dwFlags = (up ? 0x2u : 0u) | (key == 0xA3 ? 0x1u : 0u), dwExtraInfo = Marker };
        return SendInput(1, &input, sizeof(Input)) == 1;
    }

    private static void SendButtonUp()
    {
        var input = new Input { type = 0 /* INPUT_MOUSE */ };
        input.u.mi = new MouseInput { dwFlags = 0x0004 /* MOUSEEVENTF_LEFTUP */, dwExtraInfo = Marker };
        SendInput(1, &input, sizeof(Input));
    }

    /// <summary>
    /// Un movimiento de ratón de cero píxeles: el puntero no se mueve, pero el hilo del Explorador que lleva el
    /// arrastre despierta y atiende las consultas que esperan (sin él, hasta el siguiente clic del usuario). Por si
    /// acaso, también un mensaje vacío a su ventana de arrastre.
    /// </summary>
    private static void Nudge(uint thread)
    {
        var input = new Input { type = 0 /* INPUT_MOUSE */ };
        input.u.mi = new MouseInput { dwFlags = 0x0001 /* MOUSEEVENTF_MOVE, relativo: 0, 0 */, dwExtraInfo = Marker };
        SendInput(1, &input, sizeof(Input));
        if (Native.CaptureWindow(thread) is not 0 and var capture)
        {
            Native.PostMessageW(capture, 0 /* WM_NULL */, 0, 0);
        }
    }

    private sealed record Pending(nint Source, bool SourceTree, nint Target, bool TargetTree, int X, int Y, bool Ctrl, bool Shift, uint Thread, bool AwaitDrag);

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

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
