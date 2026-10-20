using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Qué hay en un punto de la pantalla según la accesibilidad de Windows (UI Automation), sin tocar el Explorador:
/// un elemento de una lista de archivos (con su nombre visible), el fondo de la lista u otra cosa.
/// Llamadas COM por la tabla de métodos, válidas con Native AOT. Debe llamarse desde un hilo STA.
/// </summary>
public static unsafe partial class UiaHitTest
{
    private static readonly Guid ClsidCuiAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid IidUiAutomation = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    private const int ListItem = 50007, List = 50008, Tree = 50023, TreeItem = 50024, DataItem = 50029;

    /// <summary>
    /// El objeto de accesibilidad, creado una vez por hilo (crearlo es lo más lento de la primera consulta).
    /// </summary>
    [ThreadStatic]
    private static nint _automation;

    private static volatile string? _pasteLabel;

    /// <summary>Prepara la accesibilidad de antemano (al empezar un arrastre), para que al soltar sea inmediata.</summary>
    public static void WarmUp() => Automation();

    /// <summary>Libera el cliente COM antes de terminar el hilo que lo creó.</summary>
    public static void ReleaseThread()
    {
        ExplorerLocator.Release((void*)_automation);
        _automation = 0;
    }

    private static void* Automation()
    {
        if (_automation == 0)
        {
            void* created = null;
            Guid clsid = ClsidCuiAutomation, iid = IidUiAutomation;
            if (CoCreateInstance(&clsid, null, 1 /* CLSCTX_INPROC_SERVER */, &iid, &created) >= 0 && created != null)
            {
                _automation = (nint)created;
            }
        }

        return (void*)_automation;
    }

    public static DropSpot At(int x, int y, out string? itemName)
    {
        itemName = null;
        void* automation = Automation();
        void* walker = null;
        void* element = null;
        if (automation == null)
        {
            return DropSpot.Unknown;
        }

        try
        {
            // IUIAutomation::ElementFromPoint (índice 7) e IUIAutomation::get_ControlViewWalker (índice 14)
            if (((delegate* unmanaged[Stdcall]<void*, Point, void**, int>)(*(void***)automation)[7])(automation, new Point(x, y), &element) < 0 || element == null
                || ((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)automation)[14])(automation, &walker) < 0 || walker == null)
            {
                return DropSpot.Unknown;
            }

            // Del elemento más profundo (el texto del nombre, el icono...) hacia arriba hasta el elemento de la lista.
            for (int depth = 0; depth < 8 && element != null; depth++)
            {
                int type = 0;
                // IUIAutomationElement::get_CurrentControlType (índice 21)
                ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)element)[21])(element, &type);
                if (type is ListItem or DataItem)
                {
                    itemName = Name(element);
                    return string.IsNullOrEmpty(itemName) ? DropSpot.Unknown : DropSpot.Item;
                }

                if (type == List)
                {
                    return DropSpot.Background;
                }

                void* parent = null;
                // IUIAutomationTreeWalker::GetParentElement (índice 3)
                ((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)(*(void***)walker)[3])(walker, element, &parent);
                ExplorerLocator.Release(element);
                element = parent;
            }

            return DropSpot.Unknown;
        }
        finally
        {
            ExplorerLocator.Release(element);
            ExplorerLocator.Release(walker); // el objeto de accesibilidad se queda para la próxima vez
        }
    }

    /// <summary>
    /// Si en ese punto hay un elemento de un árbol (el panel de navegación del Explorador), su nombre visible y los de
    /// sus padres, desde el primer nivel. Null si no lo hay.
    /// </summary>
    public static List<string>? TreePathAt(int x, int y)
    {
        void* automation = Automation();
        void* walker = null;
        void* element = null;
        if (automation == null)
        {
            return null;
        }

        try
        {
            if (((delegate* unmanaged[Stdcall]<void*, Point, void**, int>)(*(void***)automation)[7])(automation, new Point(x, y), &element) < 0 || element == null
                || ((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)automation)[14])(automation, &walker) < 0 || walker == null)
            {
                return null;
            }

            // Del elemento bajo el puntero (quizá el texto o el icono) hacia arriba: los elementos del árbol, hasta el árbol.
            var names = new List<string>();
            for (int depth = 0; depth < 64 && element != null; depth++)
            {
                int type = 0;
                ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)element)[21])(element, &type);
                if (type == TreeItem)
                {
                    string? name = Name(element);
                    if (string.IsNullOrEmpty(name))
                    {
                        return null;
                    }

                    names.Insert(0, name);
                }
                else if (type == Tree)
                {
                    return names.Count > 0 ? names : null;
                }
                else if (names.Count > 0 || depth > 3)
                {
                    return null; // algo raro entre los elementos del árbol, o no era un árbol
                }

                void* parent = null;
                ((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)(*(void***)walker)[3])(walker, element, &parent);
                ExplorerLocator.Release(element);
                element = parent;
            }

            return null;
        }
        finally
        {
            ExplorerLocator.Release(element);
            ExplorerLocator.Release(walker);
        }
    }

    /// <summary>Reconoce exclusivamente la orden Pegar habilitada de un menú del Explorador.</summary>
    public static bool IsPasteMenuAt(int x, int y, nint explorer)
    {
        void* automation = Automation();
        void* walker = null;
        void* element = null;
        if (automation == null)
        {
            return false;
        }

        try
        {
            if (((delegate* unmanaged[Stdcall]<void*, Point, void**, int>)(*(void***)automation)[7])(automation, new Point(x, y), &element) < 0
                || element == null
                || ((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)automation)[14])(automation, &walker) < 0 || walker == null)
            {
                return false;
            }

            for (int depth = 0; depth < 5 && element != null; depth++)
            {
                int type = 0, enabled = 0;
                ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)element)[21])(element, &type);
                if (type == 50011 /* MenuItem */)
                {
                    ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)element)[28])(element, &enabled);
                    string? id = StringProperty(element, 29), accelerator = StringProperty(element, 24), name = Name(element);
                    if (IsPasteCommand(id, accelerator))
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _pasteLabel = name;
                        }

                        return enabled != 0;
                    }

                    // «Mostrar más opciones» lleva a un menú clásico sin AutomationId ni acelerador en algunas
                    // versiones de Windows. Antes de dejar pasar ese clic, se aprende la etiqueta localizada de
                    // la orden canónica «paste» del menú moderno. Nunca se adivina por una traducción fija.
                    RememberPasteLabel(automation, explorer);
                    return enabled != 0 && _pasteLabel is { } label && string.Equals(name, label, StringComparison.Ordinal);
                }

                void* parent = null;
                ((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)(*(void***)walker)[3])(walker, element, &parent);
                ExplorerLocator.Release(element);
                element = parent;
            }

            return false;
        }
        finally
        {
            ExplorerLocator.Release(element);
            ExplorerLocator.Release(walker);
        }
    }

    /// <summary>Identificador del menú moderno o acelerador del clásico; nunca «Pegar acceso directo».</summary>
    public static bool IsPasteCommand(string? automationId, string? accelerator) =>
        string.Equals(automationId, "paste", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(accelerator?.Replace(" ", ""), "Ctrl+V", StringComparison.OrdinalIgnoreCase);

    private static void RememberPasteLabel(void* automation, nint explorer)
    {
        if (_pasteLabel is not null)
        {
            return;
        }

        void* root = null;
        void* condition = null;
        void* paste = null;
        nint text = Marshal.StringToBSTR("paste");
        try
        {
            // ElementFromHandle 6; CreatePropertyCondition 23; Element.FindFirst 5 (Windows SDK).
            var value = new PropertyValue { Type = 8 /* VT_BSTR */, String = text };
            if (((delegate* unmanaged[Stdcall]<void*, nint, void**, int>)(*(void***)automation)[6])(automation, explorer, &root) >= 0 && root != null
                && ((delegate* unmanaged[Stdcall]<void*, int, PropertyValue, void**, int>)(*(void***)automation)[23])(
                    automation, 30011 /* UIA_AutomationIdPropertyId */, value, &condition) >= 0 && condition != null
                && ((delegate* unmanaged[Stdcall]<void*, int, void*, void**, int>)(*(void***)root)[5])(
                    root, 4 /* TreeScope_Descendants */, condition, &paste) >= 0 && paste != null)
            {
                string? name = Name(paste);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _pasteLabel = name;
                }
            }
        }
        finally
        {
            Marshal.FreeBSTR(text);
            ExplorerLocator.Release(paste);
            ExplorerLocator.Release(condition);
            ExplorerLocator.Release(root);
        }
    }

    /// <summary>Botón Pegar de la barra del Explorador, identificado por accesibilidad, no por su posición.</summary>
    internal static ToolbarPasteTarget? ToolbarPaste(nint explorer, bool invoke = false)
    {
        void* automation = Automation();
        void* root = null;
        void* condition = null;
        void* bar = null;
        void* all = null;
        nint id = Marshal.StringToBSTR("FileExplorerCommandBar");
        try
        {
            var value = new PropertyValue { Type = 8, String = id };
            if (automation == null
                || ((delegate* unmanaged[Stdcall]<void*, nint, void**, int>)(*(void***)automation)[6])(automation, explorer, &root) < 0 || root == null
                || ((delegate* unmanaged[Stdcall]<void*, int, PropertyValue, void**, int>)(*(void***)automation)[23])(automation, 30011, value, &condition) < 0 || condition == null
                || ((delegate* unmanaged[Stdcall]<void*, int, void*, void**, int>)(*(void***)root)[5])(root, 4, condition, &bar) < 0 || bar == null)
            {
                return null;
            }

            ExplorerLocator.Release(condition);
            condition = null;
            if (((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)automation)[21])(automation, &condition) < 0 || condition == null
                || ((delegate* unmanaged[Stdcall]<void*, int, void*, void**, int>)(*(void***)bar)[6])(bar, 4, condition, &all) < 0 || all == null)
            {
                return null;
            }

            int count = 0;
            ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)all)[3])(all, &count);
            for (int i = 0; i < Math.Min(count, 100); i++)
            {
                void* element = null;
                try
                {
                    if (((delegate* unmanaged[Stdcall]<void*, int, void**, int>)(*(void***)all)[4])(all, i, &element) < 0 || element == null)
                    {
                        continue;
                    }

                    int type = 0, enabled = 0, offscreen = 1;
                    ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)element)[21])(element, &type);
                    if (type != 50000 /* Button */ || !IsToolbarPasteCommand(StringProperty(element, 29), StringProperty(element, 24), Name(element)))
                    {
                        continue;
                    }

                    ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)element)[28])(element, &enabled);
                    ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)element)[38])(element, &offscreen);
                    if (enabled == 0 || offscreen != 0)
                    {
                        return null;
                    }

                    if (invoke)
                    {
                        void* pattern = null;
                        try
                        {
                            // InvokePattern: solo para devolver al botón original un caso no gestionable.
                            if (((delegate* unmanaged[Stdcall]<void*, int, void**, int>)(*(void***)element)[16])(element, 10000, &pattern) >= 0 && pattern != null)
                            {
                                ((delegate* unmanaged[Stdcall]<void*, int>)(*(void***)pattern)[3])(pattern);
                            }
                        }
                        finally { ExplorerLocator.Release(pattern); }
                        return null;
                    }

                    var rectangle = default(UiaRect);
                    // IUIAutomationElement::get_CurrentBoundingRectangle (43), en píxeles de pantalla.
                    if (((delegate* unmanaged[Stdcall]<void*, UiaRect*, int>)(*(void***)element)[43])(element, &rectangle) >= 0
                        && rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top)
                    {
                        return new ToolbarPasteTarget(explorer, rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
                    }
                }
                finally { ExplorerLocator.Release(element); }
            }

            return null;
        }
        finally
        {
            Marshal.FreeBSTR(id);
            ExplorerLocator.Release(all);
            ExplorerLocator.Release(bar);
            ExplorerLocator.Release(condition);
            ExplorerLocator.Release(root);
        }
    }

    /// <summary>Etiquetas del control de Windows para los idiomas admitidos; no son textos de la interfaz de MegaCopier.</summary>
    internal static bool IsToolbarPasteCommand(string? id, string? accelerator, string? name) =>
        IsPasteCommand(id, accelerator) || (name is not null && (name == _pasteLabel || name is
            "Paste" or "Pegar" or "Einfügen" or "Coller" or "Incolla" or "貼り付け" or "붙여넣기" or
            "Plakken" or "Wklej" or "Colar" or "Вставить" or "Yapıştır" or "Вставити" or "粘贴" or "貼上"));

    /// <summary>
    /// La vista que el árbol de accesibilidad expone para la pestaña activa. IsWindowVisible no basta: Windows 11
    /// puede conservar visibles los HWND de otras pestañas aunque sus vistas ya no aparezcan en el árbol UIA.
    /// </summary>
    internal static nint ActiveFileView(nint explorer)
    {
        void* automation = Automation();
        void* root = null;
        void* condition = null;
        void* view = null;
        nint id = Marshal.StringToBSTR("listview");
        try
        {
            var value = new PropertyValue { Type = 8, String = id };
            if (automation != null
                && ((delegate* unmanaged[Stdcall]<void*, nint, void**, int>)(*(void***)automation)[6])(automation, explorer, &root) >= 0 && root != null
                && ((delegate* unmanaged[Stdcall]<void*, int, PropertyValue, void**, int>)(*(void***)automation)[23])(automation, 30011, value, &condition) >= 0 && condition != null
                && ((delegate* unmanaged[Stdcall]<void*, int, void*, void**, int>)(*(void***)root)[5])(root, 4, condition, &view) >= 0 && view != null)
            {
                // El panel UIA puede no tener HWND propio. El punto interior de su rectángulo identifica la
                // vista que Windows realmente presenta, incluso si conserva los HWND de pestañas anteriores.
                var rectangle = default(UiaRect);
                if (((delegate* unmanaged[Stdcall]<void*, UiaRect*, int>)(*(void***)view)[43])(view, &rectangle) >= 0
                    && rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top)
                {
                    nint window = Native.WindowFromPoint(new Native.Point(
                        rectangle.Left + (rectangle.Right - rectangle.Left) / 2,
                        rectangle.Top + (rectangle.Bottom - rectangle.Top) / 2));
                    if (Native.GetAncestor(window, 2) == explorer)
                    {
                        return Native.FindAncestorOfClass(window, "SHELLDLL_DefView");
                    }
                }
                return 0;
            }
            return 0;
        }
        finally
        {
            Marshal.FreeBSTR(id);
            ExplorerLocator.Release(view);
            ExplorerLocator.Release(condition);
            ExplorerLocator.Release(root);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UiaRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyValue
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint String;
    }

    private static string? Name(void* element) => StringProperty(element, 23);

    private static string? StringProperty(void* element, int slot)
    {
        nint bstr = 0;
        // Índices de IUIAutomationElement en UIAutomationClient.h: Name 23, AcceleratorKey 24, AutomationId 29.
        if (((delegate* unmanaged[Stdcall]<void*, nint*, int>)(*(void***)element)[slot])(element, &bstr) < 0 || bstr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);
}

internal sealed record ToolbarPasteTarget(nint Root, int Left, int Top, int Right, int Bottom)
{
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}
