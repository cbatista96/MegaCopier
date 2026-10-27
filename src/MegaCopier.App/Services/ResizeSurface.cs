using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.App.Services;

/// <summary>
/// Mantiene el HWND del contenido WinUI al tamaño completo durante el cambio del marco exterior.
/// El recorte lo hace el padre nativo: no se redimensiona el búfer de composición en cada fotograma.
/// No modifica la ventana de entrada ni sus mensajes de ratón o teclado.
/// </summary>
internal sealed unsafe class ResizeSurface : IDisposable
{
    private nint _child;
    private GCHandle _self;
    private int _width, _height;
    private const uint WindowPosChanging = 0x0046;
    private const nuint SubclassId = 0x4D43525A;

    public static ResizeSurface? TryCreate(nint parent, int width, int height)
    {
        var surface = new ResizeSurface();
        surface._self = GCHandle.Alloc(surface);
        EnumChildWindows(parent, &FindContent, GCHandle.ToIntPtr(surface._self));
        if (surface._child == 0 || !SetWindowSubclass(surface._child, &WindowProc, SubclassId, (nuint)GCHandle.ToIntPtr(surface._self)))
        { surface._self.Free(); return null; }
        surface.SetSize(width, height);
        return surface;
    }

    public void SetSize(int width, int height)
    {
        _width = Math.Max(1, width); _height = Math.Max(1, height);
        SetWindowPos(_child, 0, 0, 0, _width, _height, 0x0016 /* NOMOVE | NOZORDER | NOACTIVATE */);
    }

    public void Dispose()
    {
        if (_child != 0) { RemoveWindowSubclass(_child, &WindowProc, SubclassId); _child = 0; }
        if (_self.IsAllocated) _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FindContent(nint hwnd, nint data)
    {
        char* name = stackalloc char[256];
        int length = GetClassName(hwnd, name, 256);
        if (new ReadOnlySpan<char>(name, length).SequenceEqual("Microsoft.UI.Content.DesktopChildSiteBridge"))
        {
            ((ResizeSurface)GCHandle.FromIntPtr(data).Target!)._child = hwnd;
            return 0;
        }
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WindowPosChanging && lParam != 0)
        {
            var surface = (ResizeSurface)GCHandle.FromIntPtr((nint)data).Target!;
            var position = (WindowPosition*)lParam;
            position->Width = surface._width;
            position->Height = surface._height;
            position->Flags &= ~0x0001u; // NOSIZE: el puente conserva el tamaño de su superficie preparada.
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition { public nint Window, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern int EnumChildWindows(nint parent, delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint data);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint hwnd, char* name, int count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
}
