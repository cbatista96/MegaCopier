using System.Runtime.InteropServices;
using MegaCopier.Shell;

namespace MegaCopier.App.Services;

/// <summary>
/// Carpetas abiertas ahora mismo en ventanas del Explorador (y de Total Commander, Directory Opus, Multi Commander, xplorer2, Explorer++ y XYplorer), para ofrecerlas como destino. Usa la
/// interfaz documentada IShellWindows (la misma que usan los scripts de Windows): nada se inyecta en el Explorador.
/// Debe llamarse desde un hilo STA (el de la interfaz).
/// </summary>
internal static unsafe partial class ExplorerFolders
{
    private static readonly Guid ShellWindowsClass = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid IShellWindows = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid IServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IFolderView = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid IPersistFolder2 = new("1AC3D9F0-175C-11D1-95BE-00609797EA4F");

    /// <summary>Rutas de las carpetas abiertas (sin repetir; las que no son del disco, como "Este equipo", se omiten).</summary>
    public static List<string> Open() =>
        [.. ExplorerOnly().Concat(OtherFileManagers.Open()).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Solo las ventanas del Explorador.</summary>
    private static List<string> ExplorerOnly()
    {
        var folders = new List<string>();
        void* windows = null;
        try
        {
            Guid clsid = ShellWindowsClass, iid = IShellWindows;
            if (CoCreateInstance(&clsid, null, 4 /* CLSCTX_LOCAL_SERVER */, &iid, &windows) < 0 || windows == null)
            {
                return folders;
            }

            int count = 0;
            if (((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)windows)[7])(windows, &count) < 0) // IShellWindows::get_Count
            {
                return folders;
            }

            for (int i = 0; i < count; i++)
            {
                if (FolderOf(windows, i) is { } path && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(path);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
        }
        finally
        {
            Release(windows);
        }

        return folders;
    }

    private static string? FolderOf(void* windows, int index)
    {
        void* window = null, provider = null, browser = null, view = null, folderView = null, persist = null;
        nint pidl = 0;
        try
        {
            // IShellWindows::Item(VARIANT index, IDispatch** folder): el índice va en un VARIANT VT_I4.
            var variant = stackalloc byte[24];
            new Span<byte>(variant, 24).Clear();
            *(ushort*)variant = 3; // VT_I4
            *(int*)(variant + 8) = index;
            if (ItemAt(windows, variant, &window) < 0 || window == null)
            {
                return null;
            }

            Guid serviceIid = IServiceProvider, topLevel = TopLevelBrowser, browserIid = IShellBrowser, folderViewIid = IFolderView, persistIid = IPersistFolder2;
            if (QueryInterface(window, &serviceIid, &provider) < 0
                || ((delegate* unmanaged[Stdcall]<void*, Guid*, Guid*, void**, int>)(*(void***)provider)[3])(provider, &topLevel, &browserIid, &browser) < 0
                || ((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)browser)[15])(browser, &view) < 0 // QueryActiveShellView
                || QueryInterface(view, &folderViewIid, &folderView) < 0
                || ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)folderView)[5])(folderView, &persistIid, &persist) < 0 // GetFolder
                || ((delegate* unmanaged[Stdcall]<void*, nint*, int>)(*(void***)persist)[5])(persist, &pidl) < 0) // GetCurFolder
            {
                return null;
            }

            char* path = stackalloc char[32768];
            return SHGetPathFromIDListEx(pidl, path, 32768, 0) && path[0] != '\0' ? new string(path) : null;
        }
        finally
        {
            if (pidl != 0)
            {
                Marshal.FreeCoTaskMem(pidl);
            }

            Release(persist);
            Release(folderView);
            Release(view);
            Release(browser);
            Release(provider);
            Release(window);
        }
    }

    /// <summary>IShellWindows::Item (índice 8 de la tabla): el VARIANT se pasa por valor (24 bytes en x64, por referencia oculta).</summary>
    private static int ItemAt(void* windows, byte* variant, void** result) =>
        ((delegate* unmanaged[Stdcall]<void*, byte*, void**, int>)(*(void***)windows)[8])(windows, variant, result);

    private static int QueryInterface(void* instance, Guid* iid, void** result) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)instance)[0])(instance, iid, result);

    private static void Release(void* instance)
    {
        if (instance != null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)instance)[2])(instance);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHGetPathFromIDListEx(nint pidl, char* path, uint length, uint options);
}
