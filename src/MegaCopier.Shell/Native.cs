using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

internal static unsafe partial class Native
{
    public static string ClassName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return "";
        }

        char* buffer = stackalloc char[256];
        int length = GetClassNameW(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    public static nint FindAncestorOfClass(nint hwnd, string className)
    {
        for (nint current = hwnd; current != 0; current = GetParent(current))
        {
            if (ClassName(current) == className)
            {
                return current;
            }
        }

        return 0;
    }

    public static nint FocusedWindow(nint foreground)
    {
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { cbSize = (uint)sizeof(GuiThreadInfo) };
        return GetGUIThreadInfo(thread, &info) ? info.hwndFocus : 0;
    }

    /// <summary>
    /// En el fondo del Escritorio el foco puede quedar en Progman o WorkerW, en vez de su lista.
    /// Se resuelve únicamente esa vista; un Edit de renombrar o cualquier otro control conserva el foco.
    /// </summary>
    internal static nint DesktopFileFocus(nint foreground, nint focus)
    {
        if (ClassName(foreground) is not ("Progman" or "WorkerW")
            || (focus != 0 && ClassName(focus) is not ("Progman" or "WorkerW"))) return focus;

        nint view = FindWindowExW(foreground, 0, "SHELLDLL_DefView", null);
        return view == 0 ? focus : FindWindowExW(view, 0, "SysListView32", null);
    }

    /// <summary>La ventana que tiene capturado el ratón en ese hilo (durante un arrastre OLE, una CLIPBRDWNDCLASS).</summary>
    public static nint CaptureWindow(uint thread)
    {
        if (thread == 0)
        {
            return 0;
        }

        var info = new GuiThreadInfo { cbSize = (uint)sizeof(GuiThreadInfo) };
        return GetGUIThreadInfo(thread, &info) ? info.hwndCapture : 0;
    }

    /// <summary>Propietario real del menú activo del hilo; puede ser una vista hija, no la ventana exterior.</summary>
    public static nint MenuOwner(uint thread)
    {
        var info = new GuiThreadInfo { cbSize = (uint)sizeof(GuiThreadInfo) };
        return thread != 0 && GetGUIThreadInfo(thread, &info) ? info.hwndMenuOwner : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowRect
    {
        public int Left, Top, Right, Bottom;
        public readonly bool SameAs(WindowRect other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint window, out WindowRect rectangle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Point(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public int left, top, right, bottom;
    }

    [LibraryImport("user32.dll")]
    private static partial int GetClassNameW(nint hwnd, char* buffer, int max);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint after, string className, string? title);


    [LibraryImport("user32.dll")]
    public static partial nint GetParent(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint thread, GuiThreadInfo* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int key);
}
