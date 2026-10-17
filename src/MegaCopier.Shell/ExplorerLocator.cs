using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>Lo que muestra una vista del Explorador: su carpeta, lo seleccionado y sus elementos.</summary>
/// <param name="Folder">Carpeta del disco (null si es virtual: Este equipo, Papelera, un ZIP, una búsqueda...).</param>
/// <param name="Selection">Rutas seleccionadas (null si alguna no es del disco o no se pidió).</param>
/// <param name="Items">Elementos de la vista (vacío si no se pidió).</param>
public sealed record ViewSnapshot(string? Folder, IReadOnlyList<string>? Selection, IReadOnlyList<ViewItem> Items);

/// <summary>
/// Averigua qué muestra una ventana (o pestaña) del Explorador, o el Escritorio.
///
/// Recorre las ventanas del shell (IShellWindows), pide a cada una su IShellBrowser y compara su ventana con la
/// pestaña buscada; de la pestaña coincidente obtiene su vista (IFolderView): la carpeta (IPersistFolder2), la
/// selección y los elementos (IShellItemArray). El Escritorio se pide aparte (FindWindowSW con SWC_DESKTOP).
/// Se usan llamadas COM por la tabla de métodos (sin reflexión), así que funciona con Native AOT.
/// Nada de esto toca el Explorador por dentro: son las interfaces públicas que ofrece a otros programas.
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
    private static readonly Guid IidShellItemArray = new("B63EA76D-1F85-456F-A19C-48159EFA858B");

    private const string TabClass = "ShellTabWindowClass";
    private const uint SvgioSelection = 0x1, SvgioAllView = 0x2;
    private const uint SigdnNormalDisplay = 0, SigdnFileSysPath = 0x80058000;
    private const uint SfgaoFolder = 0x20000000, SfgaoStream = 0x00400000, SfgaoFileSystem = 0x40000000;
    private const int SwcDesktop = 8, SwfoNeedDispatch = 1;

    /// <summary>¿Es una ventana del Explorador o el Escritorio (la raíz de la ventana)?</summary>
    public static bool IsExplorerOrDesktop(nint window) =>
        Native.ClassName(Native.GetAncestor(window, 2 /* GA_ROOT */)) is "CabinetWClass" or "Progman" or "WorkerW";

    /// <summary>
    /// Carpeta del sistema de archivos que muestra la pestaña que contiene <paramref name="anyWindowInTab"/>
    /// (por ejemplo, la ventana con el foco). Null si es una ubicación virtual (Este equipo, Papelera, un ZIP...).
    /// </summary>
    public static string? GetFolderPath(nint anyWindowInTab)
    {
        if (IsDesktop(anyWindowInTab))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        return Snapshot(anyWindowInTab, selection: false, items: false)?.Folder;
    }

    /// <summary>
    /// Lo que muestra la pestaña (o el Escritorio) que contiene <paramref name="anyWindowInTab"/>.
    /// Null si no se encuentra la ventana entre las del shell.
    /// </summary>
    public static ViewSnapshot? Snapshot(nint anyWindowInTab, bool selection, bool items)
    {
        nint top = Native.GetAncestor(anyWindowInTab, 2 /* GA_ROOT */);
        bool desktop = IsDesktop(anyWindowInTab);
        nint tab = desktop ? 0 : Native.FindAncestorOfClass(anyWindowInTab, TabClass);

        void* shellWindows = null;
        Guid clsid = ClsidShellWindows, iid = IidShellWindows;
        if (CoCreateInstance(&clsid, null, 4 /* CLSCTX_LOCAL_SERVER */, &iid, &shellWindows) < 0 || shellWindows == null)
        {
            return null;
        }

        try
        {
            if (desktop)
            {
                // IShellWindows::FindWindowSW (índice 15) con SWC_DESKTOP: el Escritorio no está en la lista normal.
                var location = new Variant { vt = 3 /* VT_I4 */, intVal = 0 /* CSIDL_DESKTOP */ };
                var empty = default(Variant);
                int hwnd = 0;
                void* dispatch = null;
                if (((delegate* unmanaged[Stdcall]<void*, Variant*, Variant*, int, int*, int, void**, int>)(*(void***)shellWindows)[15])(
                        shellWindows, &location, &empty, SwcDesktop, &hwnd, SwfoNeedDispatch, &dispatch) < 0 || dispatch == null)
                {
                    return null;
                }

                try
                {
                    var snapshot = TrySnapshot(dispatch, 0, 0, selection, items);
                    return snapshot is null ? null : snapshot with { Folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
                }
                finally
                {
                    Release(dispatch);
                }
            }

            int count = 0;
            // IShellWindows::get_Count (índice 7, tras IUnknown + IDispatch)
            if (((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)shellWindows)[7])(shellWindows, &count) < 0)
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
                    var snapshot = TrySnapshot(dispatch, top, tab, selection, items);
                    if (snapshot is not null)
                    {
                        return snapshot;
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

    private static bool IsDesktop(nint window) =>
        Native.ClassName(Native.GetAncestor(window, 2 /* GA_ROOT */)) is "Progman" or "WorkerW";

    /// <summary>
    /// Null si esta ventana del shell no es la buscada (con <paramref name="topWindow"/> 0 vale cualquiera: el
    /// Escritorio). Si lo es, lo que muestra; con Folder null si es una ubicación virtual.
    /// </summary>
    private static ViewSnapshot? TrySnapshot(void* dispatch, nint topWindow, nint tabWindow, bool selection, bool items)
    {
        void* services = null;
        void* browser = null;
        void* view = null;
        void* folderView = null;
        void* persist = null;
        nint pidl = 0;
        var empty = new ViewSnapshot(null, null, []);
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

            if (topWindow != 0)
            {
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
            }

            // IShellBrowser::QueryActiveShellView (índice 15)
            if (((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)browser)[15])(browser, &view) < 0 || view == null)
            {
                return empty;
            }

            Guid iidFolderView = IidFolderView;
            if (QueryInterface(view, &iidFolderView, &folderView) < 0)
            {
                return empty;
            }

            string? folder = null;
            Guid iidPersist = IidPersistFolder2;
            // IFolderView::GetFolder (índice 5) e IPersistFolder2::GetCurFolder (índice 5)
            if (((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)folderView)[5])(folderView, &iidPersist, &persist) >= 0
                && ((delegate* unmanaged[Stdcall]<void*, nint*, int>)(*(void***)persist)[5])(persist, &pidl) >= 0 && pidl != 0)
            {
                char* buffer = stackalloc char[32768];
                if (SHGetPathFromIDListEx(pidl, buffer, 32768, 0))
                {
                    folder = new string(buffer);
                }
            }

            IReadOnlyList<string>? selected = null;
            if (selection)
            {
                var list = ReadItems(folderView, SvgioSelection);
                selected = list is not null && list.All(i => i.Path is not null) ? [.. list.Select(i => i.Path!)] : null;
            }

            return new ViewSnapshot(folder, selected, items ? ReadItems(folderView, SvgioAllView) ?? [] : []);
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

    /// <summary>Elementos de la vista (IFolderView::Items) con su nombre visible y su ruta, si son del disco.</summary>
    private static List<ViewItem>? ReadItems(void* folderView, uint which)
    {
        void* array = null;
        Guid iid = IidShellItemArray;
        // IFolderView::Items (índice 8)
        if (((delegate* unmanaged[Stdcall]<void*, uint, Guid*, void**, int>)(*(void***)folderView)[8])(folderView, which, &iid, &array) < 0 || array == null)
        {
            // Sin selección, Items(SVGIO_SELECTION) falla: es una lista vacía.
            return which == SvgioSelection ? [] : null;
        }

        try
        {
            uint count = 0;
            // IShellItemArray::GetCount (índice 7)
            if (((delegate* unmanaged[Stdcall]<void*, uint*, int>)(*(void***)array)[7])(array, &count) < 0)
            {
                return null;
            }

            var result = new List<ViewItem>((int)Math.Min(count, 100_000));
            for (uint i = 0; i < count && i < 100_000; i++)
            {
                void* item = null;
                // IShellItemArray::GetItemAt (índice 8)
                if (((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)(*(void***)array)[8])(array, i, &item) < 0 || item == null)
                {
                    continue;
                }

                try
                {
                    string name = DisplayName(item, SigdnNormalDisplay) ?? "";
                    string? path = DisplayName(item, SigdnFileSysPath);
                    uint attributes = 0;
                    // IShellItem::GetAttributes (índice 6). Un ZIP es carpeta y "stream" a la vez: no es una carpeta de verdad.
                    ((delegate* unmanaged[Stdcall]<void*, uint, uint*, int>)(*(void***)item)[6])(item, SfgaoFolder | SfgaoStream | SfgaoFileSystem, &attributes);
                    bool isFolder = (attributes & SfgaoFolder) != 0 && (attributes & SfgaoStream) == 0 && path is not null && Directory.Exists(path);
                    result.Add(new ViewItem(name, path, isFolder));
                }
                finally
                {
                    Release(item);
                }
            }

            return result;
        }
        finally
        {
            Release(array);
        }
    }

    private static string? DisplayName(void* item, uint kind)
    {
        char* name = null;
        // IShellItem::GetDisplayName (índice 5)
        if (((delegate* unmanaged[Stdcall]<void*, uint, char**, int>)(*(void***)item)[5])(item, kind, &name) < 0 || name == null)
        {
            return null;
        }

        try
        {
            return new string(name);
        }
        finally
        {
            CoTaskMemFree((nint)name);
        }
    }

    private static int QueryInterface(void* com, Guid* iid, void** result) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)com)[0])(com, iid, result);

    internal static void Release(void* com)
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
