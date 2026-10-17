using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MegaCopier.Shell;

/// <summary>
/// Registra MegaCopierShell.dll en el Explorador para el usuario actual (HKCU): no necesita permisos de administrador.
/// En Windows 11 estas opciones aparecen en "Mostrar más opciones" (al menú corto llega "Pegar con MegaCopier" por
/// el paquete disperso, ver ShellMenuPackage en la app) y directamente en el menú de arrastrar con el botón derecho.
/// </summary>
public static partial class ShellExtensionRegistration
{
    public const string DllName = "MegaCopierShell.dll";

    // Deben coincidir con MegaCopierShell.cpp.
    private const string ContextMenuClsid = "{E2B6C3A1-5F47-4C8E-A1D2-7B3F9C0E6A54}";
    private const string DragDropClsid = "{A9D4F1C7-2B6E-4A3F-8C5D-1E7B0F9A3C62}";
    private const string HandlerName = "MegaCopier";

    private static readonly string[] ContextMenuTargets =
    [
        @"Directory\Background\shellex\ContextMenuHandlers",
        @"DesktopBackground\shellex\ContextMenuHandlers",
        @"Directory\shellex\ContextMenuHandlers",
        @"Drive\shellex\ContextMenuHandlers",

        // Sobre archivos: el submenú MegaCopier (comprobar sumas, crear sumas, borrar de forma segura).
        @"*\shellex\ContextMenuHandlers",
    ];

    private static readonly string[] DragDropTargets =
    [
        @"Directory\shellex\DragDropHandlers",
        @"Drive\shellex\DragDropHandlers",
    ];

    /// <summary>Ruta de la DLL registrada, o null si no está registrada.</summary>
    public static string? RegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\CLSID\{ContextMenuClsid}\InprocServer32");
        return key?.GetValue(null) as string;
    }

    public static bool IsRegistered => RegisteredPath() is { } path && File.Exists(path);

    public static void Register(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException("No se encontró la extensión del Explorador.", dllPath);
        }

        RegisterClass(ContextMenuClsid, "MegaCopier (menú contextual)", dllPath);
        RegisterClass(DragDropClsid, "MegaCopier (arrastrar y soltar)", dllPath);

        foreach (string target in ContextMenuTargets)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{target}\{HandlerName}");
            key.SetValue(null, ContextMenuClsid);
        }

        foreach (string target in DragDropTargets)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{target}\{HandlerName}");
            key.SetValue(null, DragDropClsid);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        foreach (string target in ContextMenuTargets.Concat(DragDropTargets))
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{target}\{HandlerName}", throwOnMissingSubKey: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{ContextMenuClsid}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{DragDropClsid}", throwOnMissingSubKey: false);
        NotifyShell();
    }

    private static void RegisterClass(string clsid, string description, string dllPath)
    {
        using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{clsid}");
        root.SetValue(null, description);
        using var server = root.CreateSubKey("InprocServer32");
        server.SetValue(null, dllPath);
        server.SetValue("ThreadingModel", "Apartment");
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
