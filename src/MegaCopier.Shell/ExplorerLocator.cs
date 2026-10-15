using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Averigua qué carpeta muestra una ventana (o pestaña) del Explorador.
///
/// Recorre las ventanas del shell (IShellWindows), pide a cada una su IShellBrowser y compara su ventana con la
/// pestaña que contiene el foco; de la pestaña coincidente obtiene la carpeta de su vista (IFolderView → IPersistFolder2).
/// Se usan llamadas COM por la tabla de métodos (sin reflexión), así que funciona con Native AOT.
/// Debe llamarse desde un hilo STA.
/// </summary>
public static unsafe partial class ExplorerLocator
{
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid IidShellWindows = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid IidServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IidFolderView = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid IidPersistFolder2 = new("1AC3D9F0-175C-11D1-95BE-00609797EA4F");

    private const string TabClass = "ShellTabWindowClass";

    /// <summary>
    /// Carpeta del sistema de archivos que muestra la pestaña que contiene <paramref name="anyWindowInTab"/>
    /// (por ejemplo, la ventana con el foco). Null si es una ubicación virtual (Este equipo, Papelera, un ZIP...).
    /// </summary>
    public static string? GetFolderPath(nint anyWindowInTab)
    {
        nint top = Native.GetAncestor(anyWindowInTab, 2 /* GA_ROOT */);
        string topClass = Native.ClassName(top);
        if (topClass is "Progman" or "WorkerW")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        nint tab = Native.FindAncestorOfClass(anyWindowInTab, TabClass);
        return FindBrowserFolder(top, tab);
    }

    private static string? FindBrowserFolder(nint topWindow, nint tabWindow)
    {
        void* shellWindows = null;
        Guid clsid = ClsidShellWindows, iid = IidShellWindows;
        if (CoCreateInstance(&clsid, null, 4 /* CLSCTX_LOCAL_SERVER */, &iid, &shellWindows) < 0 || shellWindows == null)
        {
            return null;
        }

        try
        {
            int count = 0;
            // IShellWindows::get_Count (índice 7, tras IUnknown + IDispatch)
            if (Vtbl<int>(shellWindows, 7)(shellWindows, &count) < 0)
            {
                return null;
            }

            for (int i = 0; i < count; i++)
            {
                var index = new Variant { vt = 3 /* VT_I4 */, intVal = i };
                void* dispatch = null;
                // IShellWindows::Item(VARIANT, IDispatch**) (índice 8)
                if (((delegate* unmanaged[Stdcall]<void*, Variant, void**, int>)(*(void***)shellWindows)[8])(shellWindows, index, &dispatch) < 0 || dispatch == null)
                {
                    continue;
                }

                try
                {
                    string? path = TryBrowserFolder(dispatch, topWindow, tabWindow);
                    if (path is not null)
                    {
                        return path.Length == 0 ? null : path;
                    }
                }
                finally
                {
                    Release(dispatch);
                }
            }
        }
        finally
        {
            Release(shellWindows);
        }

        return null;
    }

    /// <summary>
    /// Null si esta ventana del shell no es la buscada; "" si lo es pero muestra una ubicación virtual; si no, la ruta.
    /// </summary>
    private static string? TryBrowserFolder(void* dispatch, nint topWindow, nint tabWindow)
    {
        void* services = null;
        void* browser = null;
        void* view = null;
        void* folderView = null;
        void* persist = null;
        nint pidl = 0;
        try
        {
            Guid iid = IidServiceProvider;
            if (QueryInterface(dispatch, &iid, &services) < 0)
            {
                return null;
            }

            Guid sid = SidTopLevelBrowser, iidBrowser = IidShellBrowser;
            // IServiceProvider::QueryService (índice 3)
            if (((delegate* unmanaged[Stdcall]<void*, Guid*, Guid*, void**, int>)(*(void***)services)[3])(services, &sid, &iidBrowser, &browser) < 0)
            {
                return null;
            }

            nint browserWindow = 0;
            // IOleWindow::GetWindow (índice 3): en Windows 11 es la ventana de la pestaña (ShellTabWindowClass)
            if (((delegate* unmanaged[Stdcall]<void*, nint*, int>)(*(void***)browser)[3])(browser, &browserWindow) < 0)
            {
                return null;
            }

            bool matches = tabWindow != 0
                ? browserWindow == tabWindow
                : Native.GetAncestor(browserWindow, 2) == topWindow;
            if (!matches)
            {
                return null;
            }

            // IShellBrowser::QueryActiveShellView (índice 15)
            if (((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)browser)[15])(browser, &view) < 0 || view == null)
            {
                return "";
            }

            Guid iidFolderView = IidFolderView;
            if (QueryInterface(view, &iidFolderView, &folderView) < 0)
            {
                return "";
            }

            Guid iidPersist = IidPersistFolder2;
            // IFolderView::GetFolder (índice 5)
            if (((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)folderView)[5])(folderView, &iidPersist, &persist) < 0)
            {
                return "";
            }

            // IPersistFolder2::GetCurFolder (índice 5)
            if (((delegate* unmanaged[Stdcall]<void*, nint*, int>)(*(void***)persist)[5])(persist, &pidl) < 0 || pidl == 0)
            {
                return "";
            }

            char* buffer = stackalloc char[32768];
            return SHGetPathFromIDListEx(pidl, buffer, 32768, 0) ? new string(buffer) : "";
        }
        finally
        {
            if (pidl != 0)
            {
                CoTaskMemFree(pidl);
            }

            Release(persist);
            Release(folderView);
            Release(view);
            Release(browser);
            Release(services);
        }
    }

    private static delegate* unmanaged[Stdcall]<void*, T*, int> Vtbl<T>(void* com, int slot)
        where T : unmanaged =>
        (delegate* unmanaged[Stdcall]<void*, T*, int>)(*(void***)com)[slot];

    private static int QueryInterface(void* com, Guid* iid, void** result) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)com)[0])(com, iid, result);

    private static void Release(void* com)
    {
        if (com != null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)com)[2])(com);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public int intVal;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint memory);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHGetPathFromIDListEx(nint pidl, char* path, uint length, uint options);
}
