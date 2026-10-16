using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MegaCopier.App.Services;

/// <summary>Una entrada del menú de la bandeja. Sin <see cref="Text"/> es un separador.</summary>
internal sealed record TrayMenuItem(string? Text, Action? OnClick = null, bool Checked = false, bool Enabled = true)
{
    public static TrayMenuItem Separator { get; } = new(Text: null);
}

/// <summary>
/// Icono de la bandeja del sistema con la API de Win32 (Shell_NotifyIcon), sin dependencias externas y compatible con AOT.
/// Usa una ventana de mensajes propia en el hilo de la interfaz: WinUI ya bombea sus mensajes.
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmTray = WmApp + 1;
    private const uint WmNull = 0x0000;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmTimer = 0x0113;
    private const nuint ClickTimerId = 1;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;

    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10, NifShowTip = 0x80;
    private const uint NotifyIconVersion4 = 4;
    private const uint NiifUser = 0x4, NiifLargeIcon = 0x20;

    private static readonly uint WmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private static readonly string ClassName = "MegaCopierTray";
    private static bool _classRegistered;

    private readonly Func<IReadOnlyList<TrayMenuItem>> _buildMenu;
    private readonly nint _hwnd;
    private readonly nint _normalIcon;
    private readonly nint _disabledIcon;
    private readonly nint _largeIcon;
    private nint _icon;
    private GCHandle _self;
    private string _tooltip;
    private bool _disposed;
    private long _ignoreSelectUntil;

    /// <param name="disabledIconPath">Icono para cuando MegaCopier está desactivado (gris con una X roja).</param>
    public TrayIcon(string iconPath, string disabledIconPath, string tooltip, Func<IReadOnlyList<TrayMenuItem>> buildMenu)
    {
        _buildMenu = buildMenu;
        _tooltip = tooltip;
        nint instance = GetModuleHandleW(null);

        if (!_classRegistered)
        {
            fixed (char* name = ClassName)
            {
                var wc = new WndClassExW
                {
                    cbSize = (uint)sizeof(WndClassExW),
                    lpfnWndProc = &WndProc,
                    hInstance = instance,
                    lpszClassName = name,
                };
                RegisterClassExW(&wc);
            }

            _classRegistered = true;
        }

        _hwnd = CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, -3 /* HWND_MESSAGE */, 0, instance, 0);
        _self = GCHandle.Alloc(this);
        SetWindowLongPtrW(_hwnd, -21 /* GWLP_USERDATA */, GCHandle.ToIntPtr(_self));

        _normalIcon = LoadImageW(0, iconPath, 1 /* IMAGE_ICON */, GetSystemMetrics(49), GetSystemMetrics(50), 0x10 /* LR_LOADFROMFILE */);
        _disabledIcon = LoadImageW(0, disabledIconPath, 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x10);
        _largeIcon = LoadImageW(0, iconPath, 1, GetSystemMetrics(11), GetSystemMetrics(12), 0x10);
        _icon = _normalIcon;
        Add();
    }

    /// <summary>Clic izquierdo (o Intro) sobre el icono. Se espera al tiempo de doble clic para no confundirlos.</summary>
    public event Action? Activated;

    /// <summary>Doble clic sobre el icono.</summary>
    public event Action? DoubleClicked;

    /// <summary>Muestra el icono normal o el de "desactivado".</summary>
    public void SetDisabled(bool disabled)
    {
        nint icon = disabled && _disabledIcon != 0 ? _disabledIcon : _normalIcon;
        if (icon == _icon)
        {
            return;
        }

        _icon = icon;
        var data = NewData(NifIcon);
        Shell_NotifyIconW(NimModify, &data);
    }

    public void SetTooltip(string text)
    {
        if (text == _tooltip)
        {
            return;
        }

        _tooltip = text;
        var data = NewData(NifTip | NifShowTip);
        Shell_NotifyIconW(NimModify, &data);
    }

    /// <summary>Notificación (en Windows 11 aparece como un aviso del sistema).</summary>
    public void ShowNotification(string title, string text)
    {
        var data = NewData(NifInfo);
        Copy(title, data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        data.dwInfoFlags = NiifUser | NiifLargeIcon;
        data.hBalloonIcon = _largeIcon;
        Shell_NotifyIconW(NimModify, &data);
    }

    /// <summary>Hace que el menú nativo siga el tema (oscuro/claro) de la app.</summary>
    public static void SetMenuTheme(bool? dark)
    {
        // uxtheme ordinales 135 (SetPreferredAppMode) y 136 (FlushMenuThemes): no documentados, pero estables desde 1903.
        nint uxtheme = LoadLibraryW("uxtheme.dll");
        if (uxtheme == 0)
        {
            return;
        }

        var setPreferredAppMode = (delegate* unmanaged<int, int>)GetProcAddress(uxtheme, (byte*)135);
        var flushMenuThemes = (delegate* unmanaged<void>)GetProcAddress(uxtheme, (byte*)136);
        if (setPreferredAppMode != null)
        {
            setPreferredAppMode(dark switch { true => 2 /* ForceDark */, false => 3 /* ForceLight */, null => 1 /* AllowDark */ });
        }

        if (flushMenuThemes != null)
        {
            flushMenuThemes();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var data = NewData(0);
        Shell_NotifyIconW(NimDelete, &data);
        DestroyWindow(_hwnd);
        if (_normalIcon != 0)
        {
            DestroyIcon(_normalIcon);
        }

        if (_disabledIcon != 0)
        {
            DestroyIcon(_disabledIcon);
        }

        if (_largeIcon != 0)
        {
            DestroyIcon(_largeIcon);
        }

        _self.Free();
    }

    private void Add()
    {
        var data = NewData(NifMessage | NifIcon | NifTip | NifShowTip);
        Shell_NotifyIconW(NimAdd, &data);
        data.uVersion = NotifyIconVersion4;
        Shell_NotifyIconW(NimSetVersion, &data);
    }

    private NotifyIconDataW NewData(uint flags)
    {
        var data = new NotifyIconDataW
        {
            cbSize = (uint)sizeof(NotifyIconDataW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WmTray,
            hIcon = _icon,
        };
        Copy(_tooltip, data.szTip, 128);
        return data;
    }

    private static void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    private void ShowMenu()
    {
        var items = _buildMenu();
        nint menu = CreatePopupMenu();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Text is null)
                {
                    AppendMenuW(menu, 0x800 /* MF_SEPARATOR */, 0, null);
                    continue;
                }

                uint flags = (item.Checked ? 0x8u /* MF_CHECKED */ : 0) | (item.Enabled ? 0 : 0x1u /* MF_GRAYED */);
                AppendMenuW(menu, flags, (nuint)(i + 1), item.Text);
            }

            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd); // sin esto el menú no se cierra al hacer clic fuera
            int command = TrackPopupMenuEx(menu, 0x100 /* TPM_RETURNCMD */ | 0x2 /* TPM_RIGHTBUTTON */, point.X, point.Y, _hwnd, 0);
            PostMessageW(_hwnd, WmNull, 0, 0);

            if (command > 0 && command <= items.Count)
            {
                items[command - 1].OnClick?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            nint userData = GetWindowLongPtrW(hwnd, -21);
            if (userData != 0 && GCHandle.FromIntPtr(userData).Target is TrayIcon tray)
            {
                if (message == WmTray)
                {
                    uint trayEvent = (uint)(lParam & 0xFFFF);
                    if (trayEvent == WmLButtonDblClk)
                    {
                        // Doble clic: anula el clic simple pendiente y el que llega justo después.
                        KillTimer(hwnd, ClickTimerId);
                        tray._ignoreSelectUntil = Environment.TickCount64 + GetDoubleClickTime();
                        tray.DoubleClicked?.Invoke();
                    }
                    else if (trayEvent is NinSelect or NinKeySelect)
                    {
                        if (trayEvent == NinKeySelect)
                        {
                            tray.Activated?.Invoke();
                        }
                        else if (Environment.TickCount64 >= tray._ignoreSelectUntil)
                        {
                            SetTimer(hwnd, ClickTimerId, GetDoubleClickTime(), 0);
                        }
                    }
                    else if (trayEvent is WmContextMenu or WmRButtonUp)
                    {
                        tray.ShowMenu();
                    }

                    return 0;
                }

                if (message == WmTimer && wParam == (nint)ClickTimerId)
                {
                    KillTimer(hwnd, ClickTimerId);
                    tray.Activated?.Invoke();
                    return 0;
                }

                if (message == WmTaskbarCreated)
                {
                    tray.Add(); // el Explorador se reinició: hay que volver a poner el icono
                    return 0;
                }
            }
        }
        catch
        {
            // Una excepción no puede cruzar a código nativo.
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassExW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconDataW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WndClassExW* windowClass);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryW(string fileName);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetProcAddress(nint module, byte* procName);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NotifyIconDataW* data);

    [LibraryImport("user32.dll")]
    private static partial nuint SetTimer(nint hwnd, nuint id, uint elapse, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
}
