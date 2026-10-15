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

    [LibraryImport("user32.dll")]
    public static partial nint GetParent(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

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
