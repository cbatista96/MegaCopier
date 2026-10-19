using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Traduce un elemento del panel de navegación del Explorador (su nombre visible y los de sus padres, tal como los da
/// la accesibilidad: «Este equipo» › «Disco local (C:)» › «Usuarios») a su carpeta del disco, recorriendo el espacio de
/// nombres del shell desde fuera (IShellItem e IEnumShellItems, en este proceso): nada toca el Explorador por dentro.
/// Los nombres se comparan con los que muestra el shell, así que valen las carpetas con nombre traducido («Usuarios»,
/// «Descargas»), las unidades con su etiqueta y las carpetas ancladas en Inicio. Lo que no es una carpeta del disco
/// (Red, un móvil, Galería) da null. Debe llamarse desde un hilo STA.
/// </summary>
public static unsafe partial class ShellNamespace
{
    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid BhidEnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");
    private static readonly Guid IidEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");

    private const uint SigdnNormalDisplay = 0, SigdnDesktopAbsoluteParsing = 0x80028000, SigdnFileSysPath = 0x80058000;
    private const uint SfgaoFolder = 0x20000000, SfgaoStream = 0x00400000;

    /// <summary>
    /// Inicio y Acceso rápido (sus carpetas ancladas aparecen en el primer nivel del panel) y Este equipo (sus unidades,
    /// por si la accesibilidad diera solo el nombre de la unidad, sin «Este equipo» delante).
    /// </summary>
    private static readonly string[] TopLevelRoots =
        ["::{F874310E-B6B7-47DC-BC84-B9E6B38F5903}", "::{679F85CB-0220-4080-B29B-5540CC05AAB6}", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"];

    /// <summary>Un elemento del shell: nombre visible, carpeta del disco (si la tiene) y nombre para volver a abrirlo.</summary>
    private sealed record Node(string Name, string? Path, string Parsing);

    private static (List<Node> Nodes, long Taken) _topLevel = ([], 0);

    /// <summary>
    /// Lee de antemano el primer nivel del panel (lo más lento: Inicio enumera también los recientes), para que al
    /// soltar solo falte bajar por las carpetas. Se renueva si tiene más de un minuto.
    /// </summary>
    public static void WarmUp() => TopLevel();

    /// <param name="names">Nombres visibles desde el primer nivel del panel hasta el elemento.</param>
    /// <returns>La carpeta del disco, o null si no lo es o hay dudas (dos elementos distintos con el mismo nombre).</returns>
    public static string? Resolve(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return null;
        }

        Node? current = null;
        for (int i = 0; i < names.Count; i++)
        {
            current = i == 0 ? Pick(TopLevel(), names[0]) : Child(current!, names[i]);
            if (current is null)
            {
                return null;
            }
        }

        return current!.Path is { } path && Directory.Exists(path) ? path : null;
    }

    private static List<Node> TopLevel()
    {
        if (_topLevel.Nodes.Count > 0 && Stopwatch.GetElapsedTime(_topLevel.Taken) < TimeSpan.FromMinutes(1))
        {
            return _topLevel.Nodes;
        }

        // El Escritorio (la raíz del shell) y lo que cuelga de él: Este equipo, Red, la carpeta del usuario, OneDrive...
        var nodes = new List<Node>();
        if (Root() is { } root)
        {
            nodes.Add(root);
            nodes.AddRange(Children(""));
        }

        foreach (string parsing in TopLevelRoots)
        {
            nodes.AddRange(Children(parsing));
        }

        _topLevel = (nodes, Stopwatch.GetTimestamp());
        return nodes;
    }

    /// <summary>El elemento con ese nombre; si hay varios, solo vale si todos son la misma carpeta.</summary>
    private static Node? Pick(IEnumerable<Node> nodes, string name)
    {
        var matches = nodes.Where(n => string.Equals(n.Name, name, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        var first = matches[0];
        return matches.All(m => m.Parsing == first.Parsing || (m.Path is not null && string.Equals(m.Path, first.Path, StringComparison.OrdinalIgnoreCase)))
            ? first
            : null;
    }

    private static Node? Child(Node parent, string name)
    {
        // Lo normal: una carpeta del disco cuyo nombre visible es el suyo (sin enumerar la carpeta entera).
        if (parent.Path is { } folder && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0)
        {
            string direct = System.IO.Path.Combine(folder, name);
            if (Directory.Exists(direct) && string.Equals(VisibleName(direct), name, StringComparison.CurrentCultureIgnoreCase))
            {
                return new Node(name, direct, direct);
            }
        }

        return Pick(Children(parent.Parsing), name);
    }

    private static Node? Root()
    {
        void* item = CreateItem("");
        if (item == null)
        {
            return null;
        }

        try
        {
            return Describe(item, "");
        }
        finally
        {
            ExplorerLocator.Release(item);
        }
    }

    /// <summary>El elemento del shell con ese nombre de análisis; "" es la raíz (el Escritorio).</summary>
    private static void* CreateItem(string parsing)
    {
        void* item = null;
        Guid iid = IidShellItem;
        if (parsing.Length == 0)
        {
            // Una lista de identificadores vacía es la raíz del shell.
            ushort empty = 0;
            return SHCreateItemFromIDList(&empty, &iid, &item) < 0 ? null : item;
        }

        return SHCreateItemFromParsingName(parsing, null, &iid, &item) < 0 ? null : item;
    }

    /// <summary>Las carpetas (no archivos ni ZIP) dentro del elemento; "" es la raíz del shell.</summary>
    private static List<Node> Children(string parsing)
    {
        var result = new List<Node>();
        void* item = CreateItem(parsing);
        void* enumerator = null;
        try
        {
            Guid bhid = BhidEnumItems, iidEnum = IidEnumShellItems;
            // IShellItem::BindToHandler (índice 3)
            if (item == null || ((delegate* unmanaged[Stdcall]<void*, void*, Guid*, Guid*, void**, int>)(*(void***)item)[3])(item, null, &bhid, &iidEnum, &enumerator) < 0 || enumerator == null)
            {
                return result;
            }

            for (int i = 0; i < 5000; i++)
            {
                void* child = null;
                uint fetched = 0;
                // IEnumShellItems::Next (índice 3)
                if (((delegate* unmanaged[Stdcall]<void*, uint, void**, uint*, int>)(*(void***)enumerator)[3])(enumerator, 1, &child, &fetched) != 0 || fetched == 0 || child == null)
                {
                    break;
                }

                try
                {
                    uint attributes = 0;
                    // IShellItem::GetAttributes (índice 6). Un ZIP es carpeta y "stream" a la vez: no es una carpeta.
                    ((delegate* unmanaged[Stdcall]<void*, uint, uint*, int>)(*(void***)child)[6])(child, SfgaoFolder | SfgaoStream, &attributes);
                    if ((attributes & SfgaoFolder) != 0 && (attributes & SfgaoStream) == 0 && Describe(child, null) is { } node)
                    {
                        result.Add(node);
                    }
                }
                finally
                {
                    ExplorerLocator.Release(child);
                }
            }
        }
        finally
        {
            ExplorerLocator.Release(enumerator);
            ExplorerLocator.Release(item);
        }

        return result;
    }

    private static Node? Describe(void* item, string? parsing)
    {
        string? name = DisplayName(item, SigdnNormalDisplay);
        parsing ??= DisplayName(item, SigdnDesktopAbsoluteParsing);
        return name is null || parsing is null ? null : new Node(name, DisplayName(item, SigdnFileSysPath), parsing);
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
            Marshal.FreeCoTaskMem((nint)name);
        }
    }

    /// <summary>El nombre con el que el Explorador muestra una carpeta («Usuarios» para C:\Users).</summary>
    private static string? VisibleName(string path)
    {
        var info = default(FileInfo);
        return SHGetFileInfoW(path, 0, &info, (uint)sizeof(FileInfo), 0x200 /* SHGFI_DISPLAYNAME */) != 0 ? new string(info.DisplayName) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, void* bindContext, Guid* iid, void** item);

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateItemFromIDList(void* pidl, Guid* iid, void** item);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfoW(string path, uint attributes, FileInfo* info, uint size, uint flags);
}
