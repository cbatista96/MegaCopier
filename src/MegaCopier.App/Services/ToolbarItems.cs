namespace MegaCopier.App.Services;

/// <summary>
/// Accesos de la barra de herramientas: los que hay, su orden de serie y cuáles se ven de serie. El usuario los muestra,
/// oculta y ordena en Ajustes › Interfaz; los que no caben en la ventana pasan al menú «…» del final.
/// </summary>
internal static class ToolbarItems
{
    public const string Sidebar = "sidebar";
    public const string New = "new";
    public const string Separator1 = "sep1";
    public const string PlayPause = "playpause";
    public const string ClearDone = "clear";
    public const string Separator2 = "sep2";
    public const string Timeline = "timeline";
    public const string History = "history";
    public const string Settings = "settings";
    public const string About = "about";
    public const string AlwaysOnTop = "ontop";
    public const string Shelf = "shelf";
    public const string Compare = "compare";

    /// <summary>Orden de serie: el de siempre y, al final, los accesos nuevos (ocultos de serie).</summary>
    public static readonly string[] Default =
        [Sidebar, New, Separator1, PlayPause, ClearDone, Separator2, Timeline, History, Settings, About, AlwaysOnTop, Shelf, Compare];

    /// <summary>Ocultos de serie: los añadidos después, para que la barra se vea igual que antes.</summary>
    public static readonly string[] DefaultHidden = [AlwaysOnTop, Shelf, Compare];

    public static bool IsSeparator(string id) => id is Separator1 or Separator2;

    /// <summary>Ajustes no se puede ocultar: sin él no se podría volver a mostrar nada.</summary>
    public static bool CanHide(string id) => id != Settings;

    /// <summary>Icono de cada acceso (el mismo que en la barra).</summary>
    public static string Glyph(string id) => id switch
    {
        Sidebar => "",
        New => "",
        PlayPause => "",
        ClearDone => "",
        Timeline => "",
        History => "",
        Settings => "",
        About => "",
        AlwaysOnTop => "",
        Shelf => "",
        Compare => "",
        _ => "", // separador
    };

    /// <summary>Nombre de cada acceso en Ajustes y en el menú «…».</summary>
    public static string Label(string id) => L.Get(id switch
    {
        Sidebar => "Toolbar_Sidebar",
        New => "Toolbar_New",
        PlayPause => "Toolbar_PlayPause",
        ClearDone => "Toolbar_ClearDone",
        Timeline => "Toolbar_Timeline",
        History => "Toolbar_History",
        Settings => "Toolbar_Settings",
        About => "Toolbar_About",
        AlwaysOnTop => "Toolbar_AlwaysOnTop",
        Shelf => "Toolbar_Shelf",
        Compare => "Toolbar_Compare",
        _ => "Toolbar_Separator",
    });

    /// <summary>El orden guardado, sin accesos desconocidos ni repetidos y con los que falten en su sitio de serie.</summary>
    public static List<string> Order(AppSettings settings)
    {
        var order = settings.ToolbarOrder.Where(id => Default.Contains(id)).Distinct().ToList();
        for (int i = 0; i < Default.Length; i++)
        {
            if (!order.Contains(Default[i]))
            {
                // Detrás del que lo precede en el orden de serie (o al principio).
                int after = i == 0 ? -1 : order.IndexOf(Default[i - 1]);
                order.Insert(after + 1, Default[i]);
            }
        }

        return order;
    }

    public static bool IsVisible(AppSettings settings, string id) =>
        !CanHide(id) || !(settings.ToolbarHidden ?? [.. DefaultHidden]).Contains(id);

    public static void SetVisible(AppSettings settings, string id, bool visible)
    {
        var hidden = settings.ToolbarHidden ?? [.. DefaultHidden];
        hidden.Remove(id);
        if (!visible && CanHide(id))
        {
            hidden.Add(id);
        }

        settings.ToolbarHidden = hidden;
    }

    /// <summary>Lo que se ve, en orden, sin separadores al principio, al final ni dos seguidos.</summary>
    public static List<string> Visible(AppSettings settings)
    {
        var shown = new List<string>();
        foreach (string id in Order(settings).Where(id => IsVisible(settings, id)))
        {
            if (IsSeparator(id) && (shown.Count == 0 || IsSeparator(shown[^1])))
            {
                continue;
            }

            shown.Add(id);
        }

        while (shown.Count > 0 && IsSeparator(shown[^1]))
        {
            shown.RemoveAt(shown.Count - 1);
        }

        return shown;
    }
}
