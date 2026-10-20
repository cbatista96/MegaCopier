using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Un gancho de bajo nivel (ratón o teclado) en un hilo propio, con su bucle de mensajes. Windows llama al gancho en
/// el hilo que lo instaló y, si ese hilo tarda en responder (la interfaz ocupada redibujando una copia, por ejemplo),
/// lo da por colgado y lo quita sin avisar: entonces Ctrl+V o el arrastre dejan de pasar por MegaCopier «a veces».
/// En un hilo que no hace otra cosa, responde siempre al momento.
/// </summary>
internal sealed unsafe partial class HookThread
{
    private readonly int _hookType;
    private readonly nint _proc;
    private readonly string _name;

    public HookThread(int hookType, delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc, string name)
    {
        _hookType = hookType;
        _proc = (nint)proc;
        _name = name;
    }

    private const uint WmQuit = 0x0012, WmTimer = 0x0113;
    private const int WhKeyboardLl = 13;
    private const uint WatchMilliseconds = 2000;

    private long _lastCall;
    private Point _lastCursor;

    /// <summary>Se dispara (en el hilo del gancho) cuando Windows lo había quitado y se ha vuelto a poner.</summary>
    public event Action<string>? Reinstalled;

    /// <summary>El gancho avisa de que le ha llegado algo (lo llama el procedimiento del gancho, cada vez).</summary>
    public void Touch() => Volatile.Write(ref _lastCall, Environment.TickCount64);

    /// <summary>
    /// ¿Lo ha quitado Windows? El de ratón: el puntero se ha movido y al gancho no le ha llegado nada. El de teclado: ha
    /// habido algo del teclado (actividad sin mover el puntero) y al gancho no le ha llegado.
    /// </summary>
    private bool Lost()
    {
        long quiet = Environment.TickCount64 - Volatile.Read(ref _lastCall);
        GetCursorPos(out var cursor);
        bool moved = cursor.X != _lastCursor.X || cursor.Y != _lastCursor.Y;
        _lastCursor = cursor;
        if (quiet < WatchMilliseconds)
        {
            return false;
        }

        if (_hookType != WhKeyboardLl)
        {
            return moved;
        }

        var input = new LastInputInfo { Size = (uint)sizeof(LastInputInfo) };
        return !moved && GetLastInputInfo(&input) && (uint)Environment.TickCount - input.Time < WatchMilliseconds;
    }

    private Thread? _thread;
    private uint _threadId;
    private volatile bool _installed;

    public bool IsInstalled => _installed;

    /// <summary>Instala el gancho (espera a que esté puesto). Lanza si Windows no lo permite.</summary>
    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        using var ready = new ManualResetEventSlim();
        int error = 0;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            PeekMessageW(out _, 0, 0, 0, 0); // crea la cola de mensajes del hilo
            nint hook = SetWindowsHookExW(_hookType, (delegate* unmanaged[Stdcall]<int, nint, nint, nint>)_proc, GetModuleHandleW(null), 0);
            error = Marshal.GetLastPInvokeError();
            _installed = hook != 0;
            Touch();
            GetCursorPos(out _lastCursor);
            ready.Set();
            if (hook == 0)
            {
                return;
            }

            // Vigilancia: si Windows quita el gancho (lo hace sin avisar cuando una vez tarda en responder, por ejemplo
            // en una pausa del recolector de memoria con copias en marcha), se vuelve a poner.
            nuint timer = SetTimer(0, 0, WatchMilliseconds, 0);
            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                if (message.message == WmTimer && message.hwnd == 0 && Lost())
                {
                    UnhookWindowsHookEx(hook);
                    hook = SetWindowsHookExW(_hookType, (delegate* unmanaged[Stdcall]<int, nint, nint, nint>)_proc, GetModuleHandleW(null), 0);
                    _installed = hook != 0;
                    Touch();
                    Reinstalled?.Invoke(_installed ? "puesto de nuevo" : $"no se pudo poner de nuevo (error {Marshal.GetLastPInvokeError()})");
                    if (hook == 0)
                    {
                        break;
                    }

                    continue;
                }

                TranslateMessage(&message);
                DispatchMessageW(&message);
            }

            KillTimer(0, timer);
            _installed = false;
            if (hook != 0)
            {
                UnhookWindowsHookEx(hook);
            }
        })
        {
            IsBackground = true,
            Name = _name,
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
        ready.Wait();
        if (!_installed)
        {
            _thread = null;
            throw new InvalidOperationException($"No se pudo instalar el gancho (error {error}).");
        }
    }

    public void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _installed = false;
        PostThreadMessageW(_threadId, WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
        public uint private_;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int hookType, delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg message, nint window, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint window, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Msg* message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Msg* message);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint thread, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(nint window, nuint id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(LastInputInfo* info);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
