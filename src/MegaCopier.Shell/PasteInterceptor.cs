using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>Un Ctrl+V del Explorador que MegaCopier se queda.</summary>
public sealed record PasteRequest(IReadOnlyList<string> Sources, string Destination, ClipboardOperation Operation);

/// <summary>
/// Sustituye el Ctrl+V del Explorador por MegaCopier sin inyectar nada en el Explorador.
///
/// Un gancho de teclado de bajo nivel mira cada Ctrl+V. Solo lo retiene si en ese momento el foco está en la lista
/// de archivos de una ventana del Explorador (o del Escritorio) y el portapapeles contiene archivos: comprobaciones
/// instantáneas, porque Windows desactiva los ganchos lentos. El resto (leer el portapapeles, averiguar la carpeta
/// por COM) se hace en un hilo aparte. Si al final no es un caso para MegaCopier (cortar/mover, carpeta virtual,
/// pegar en la misma carpeta...), se le devuelve el pegado al Explorador tal cual.
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
    private nint _hook;
    private bool _swallowKeyUp;

    /// <param name="handler">
    /// Recibe la petición en el hilo de trabajo. Devuelve true si MegaCopier la acepta; false para que pegue el Explorador.
    /// </param>
    public PasteInterceptor(Func<PasteRequest, bool> handler)
    {
        _handler = handler;
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "MegaCopier.Paste" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>Se dispara (en el hilo de trabajo) cuando un pegado se devuelve al Explorador, con el motivo.</summary>
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
        _hook = SetWindowsHookExW(WhKeyboardLl, &HookProc, GetModuleHandleW(null), 0);
        if (_hook == 0)
        {
            throw new InvalidOperationException($"No se pudo instalar el gancho de teclado (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public void Disable()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    public void Dispose()
    {
        Disable();
        _work.CompleteAdding();
    }

    /// <summary>
    /// ¿Hay que quedarse con este Ctrl+V? Devuelve la ventana con el foco (la vista del Explorador) o 0.
    /// Solo comprobaciones baratas: se ejecuta dentro del gancho.
    /// </summary>
    internal static nint ShouldIntercept()
    {
        if (Pressed(VkShift) || Pressed(VkMenu) || Pressed(VkLWin) || Pressed(VkRWin))
        {
            return 0;
        }

        nint foreground = Native.GetForegroundWindow();
        string topClass = Native.ClassName(foreground);
        if (topClass is not ("CabinetWClass" or "Progman" or "WorkerW"))
        {
            return 0;
        }

        nint focus = Native.FocusedWindow(foreground);
        string focusClass = Native.ClassName(focus);

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
        if (code >= 0 && self is not null)
        {
            try
            {
                var data = (KbdLlHookStruct*)lParam;
                if (data->vkCode == VkV)
                {
                    uint message = (uint)wParam;
                    if (message is WmKeyDown && Pressed(VkControl))
                    {
                        nint focus = ShouldIntercept();
                        if (focus != 0)
                        {
                            self._swallowKeyUp = true;
                            self._work.TryAdd(focus);
                            return 1; // el Explorador no ve este Ctrl+V
                        }
                    }
                    else if (message is WmKeyUp && self._swallowKeyUp)
                    {
                        self._swallowKeyUp = false;
                        return 1;
                    }
                }
            }
            catch
            {
                // Una excepción no puede cruzar a código nativo; ante la duda, que el teclado funcione como siempre.
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private void WorkLoop()
    {
        foreach (nint focus in _work.GetConsumingEnumerable())
        {
            string? reason = Decide(focus, out var request);
            if (reason is null && request is not null)
            {
                try
                {
                    if (_handler(request))
                    {
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

    private static string? Decide(nint focus, out PasteRequest? request)
    {
        request = null;
        var clipboard = ClipboardFiles.TryRead();
        if (clipboard is null)
        {
            return "el portapapeles no contiene archivos";
        }

        if (clipboard.Operation == ClipboardOperation.Move)
        {
            return "mover (Ctrl+X) todavía lo hace el Explorador";
        }

        string? folder = ExplorerLocator.GetFolderPath(focus);
        if (folder is null)
        {
            return "la carpeta de destino no es una carpeta del disco";
        }

        string normalized = Path.TrimEndingDirectorySeparator(folder);
        if (clipboard.Paths.All(p => string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(p)), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return "pegar en la misma carpeta crea \"- copia\": lo hace el Explorador";
        }

        if (clipboard.Paths.Any(p => !File.Exists(p) && !Directory.Exists(p)))
        {
            return "algún elemento del portapapeles ya no existe";
        }

        request = new PasteRequest(clipboard.Paths, folder, clipboard.Operation);
        return null;
    }

    /// <summary>Devuelve el pegado al Explorador: la misma orden que su menú "Pegar".</summary>
    private static void PasteInExplorer(nint focus)
    {
        nint view = Native.FindAncestorOfClass(focus, "SHELLDLL_DefView");
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

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int hookType, delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
