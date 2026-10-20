using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Localiza Pegar en las barras del Explorador desde un STA propio. El gancho consulta solo la caché geométrica y
/// retiene el clic completo sobre ese botón: Windows nunca recibe un clic que ya se haya entregado a MegaCopier.
/// La accesibilidad vuelve a localizar el botón al devolver un caso especial al Explorador, sin mover el puntero.
/// </summary>
internal sealed unsafe partial class ExplorerPasteToolbar(Action<PasteRequest> handler, Action<string> trace) : IDisposable
{
    private sealed record Cached(ToolbarPasteTarget Target, Native.WindowRect Window, long Read);
    private sealed record Click(ToolbarPasteTarget Target, nint View);
    private volatile Cached[] _buttons = [];
    private readonly ConcurrentQueue<Click> _clicks = new();
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _enabled, _disposed;
    private Thread? _thread;
    private Click? _pressed;

    public void Enable()
    {
        _enabled = true;
        if (_thread is null)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "MegaCopier.PasteToolbar" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        _wake.Set();
    }

    public void Disable()
    {
        _enabled = false;
        _buttons = [];
        // Si se retenía un botón, se conserva hasta soltarlo para no entregar un «soltar» huérfano.
        _wake.Set();
    }

    public void Dispose()
    {
        Disable();
        _disposed = true;
        _wake.Set();
    }

    public bool OnMouse(uint message, int x, int y, nint window)
    {
        if (message == 0x0202 /* WM_LBUTTONUP */ && _pressed is { } pressed)
        {
            _pressed = null;
            if (pressed.Target.Contains(x, y))
            {
                _clicks.Enqueue(pressed);
                _wake.Set();
            }
            return true;
        }

        if (!_enabled || message != 0x0201 /* WM_LBUTTONDOWN */ || !ClipboardFiles.HasFiles())
        {
            return false;
        }

        nint root = Native.GetAncestor(window, 2);
        foreach (var cached in _buttons)
        {
            if (cached.Target.Root != root || !cached.Target.Contains(x, y)
                || Stopwatch.GetElapsedTime(cached.Read) > TimeSpan.FromMilliseconds(1500)
                || !Native.GetWindowRect(root, out var bounds) || !bounds.SameAs(cached.Window))
            {
                continue;
            }

            // El foco aún pertenece a la pestaña desde la que se hizo clic: retuvimos el botón antes de que
            // Windows lo entregue a la barra. Si el foco estaba en direcciones, se resuelve la vista por UIA.
            nint view = Native.FindAncestorOfClass(Native.FocusedWindow(root), "SHELLDLL_DefView");
            _pressed = new Click(cached.Target, view);
            return true;
        }
        _wake.Set();
        return false;
    }

    private void Run()
    {
        try
        {
            while (!_disposed)
            {
                while (_clicks.TryDequeue(out var click))
                {
                    Handle(click);
                }

                if (_enabled && ClipboardFiles.HasFiles())
                {
                    var found = new List<Cached>();
                    var roots = new List<nint>();
                    var handle = GCHandle.Alloc(roots);
                    try { EnumWindows(&Collect, GCHandle.ToIntPtr(handle)); }
                    finally { handle.Free(); }
                    foreach (nint root in roots)
                    {
                        if (!_enabled || _disposed) break;
                        if (Native.GetWindowRect(root, out var before) && UiaHitTest.ToolbarPaste(root) is { } target
                            && Native.GetWindowRect(root, out var after) && before.SameAs(after))
                        {
                            found.Add(new Cached(target, after, Stopwatch.GetTimestamp()));
                        }
                    }
                    _buttons = _enabled ? [.. found] : [];
                }
                else
                {
                    _buttons = [];
                }
                _wake.WaitOne(150);
            }
        }
        catch (Exception ex)
        {
            trace($"Pegar de la barra: {ex.Message}");
        }
        finally
        {
            _buttons = [];
            UiaHitTest.ReleaseThread();
            _wake.Dispose();
        }
    }

    private void Handle(Click click)
    {
        try
        {
            var clipboard = ClipboardFiles.TryRead();
            // Se retuvo todo el clic, incluida la activación nativa de una ventana que estuviera detrás.
            // Antes de localizar el interior de la vista se reproduce esa activación, sin mover el puntero.
            if (click.View == 0 && Native.GetForegroundWindow() != click.Target.Root)
            {
                Native.SetForegroundWindow(click.Target.Root);
            }
            nint view = click.View != 0 ? click.View : UiaHitTest.ActiveFileView(click.Target.Root);
            string? destination = view == 0 ? null : ExplorerLocator.GetFolderPath(view);
            if (_enabled && clipboard is not null && destination is not null && Directory.Exists(destination)
                && clipboard.Paths.All(p => File.Exists(p) || Directory.Exists(p))
                && !clipboard.Paths.Any(p => string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(p)),
                    Path.TrimEndingDirectorySeparator(destination), StringComparison.OrdinalIgnoreCase)))
            {
                handler(new PasteRequest(clipboard.Paths, destination, clipboard.Operation));
                trace($"Pegar de la barra superior: {clipboard.Paths.Count} elemento(s) a {destination}");
                return;
            }
        }
        catch (Exception ex)
        {
            // No se repite una petición cuya aceptación pueda haber comenzado antes de fallar.
            trace($"Pegar de la barra: {ex.Message}");
            return;
        }

        UiaHitTest.ToolbarPaste(click.Target.Root, invoke: true); // el botón original resuelve los casos especiales
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint window, nint parameter)
    {
        try
        {
            if (Native.ClassName(window) == "CabinetWClass" && Native.IsWindowVisible(window))
            {
                ((List<nint>)GCHandle.FromIntPtr(parameter).Target!).Add(window);
            }
        }
        catch { /* ninguna excepción puede cruzar el límite nativo */ }
        return 1;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint parameter);
}
