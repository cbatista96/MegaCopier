using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using MegaCopier.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace MegaCopier.App.Services;

/// <summary>
/// Los iconos de los menús y de las pestañas con el conjunto de iconos elegido. Esos controles de WinUI solo admiten un
/// icono de fuente o una imagen (no un <see cref="Controls.McIcon"/>), así que con un conjunto que no sea el de Windows
/// el icono se dibuja con <see cref="IconPainter"/> y se pone como imagen; con el de Windows se queda el de la fuente.
/// Se escribe en el XAML como siempre (un FontIcon con su Glyph): aquí se recuerda ese glifo y se cambia al abrir el
/// menú o al cambiar de conjunto o de tema.
/// </summary>
internal static class ThemedIcons
{
    /// <summary>
    /// El glifo original de cada elemento (el FontIcon que tenía en el XAML), guardado en el propio elemento: en una
    /// tabla por objeto de .NET se perdía cuando .NET liberaba ese objeto (el elemento sigue en el menú y vuelve con otro
    /// objeto), y el menú se quedaba con los iconos del conjunto de antes.
    /// </summary>
    private static readonly DependencyProperty OriginalGlyphProperty =
        DependencyProperty.RegisterAttached("OriginalGlyph", typeof(string), typeof(ThemedIcons), new PropertyMetadata(null));

    /// <summary>Los menús enganchados y la escala de pantalla con la que se abrieron la última vez.</summary>
    private static readonly ConditionalWeakTable<MenuFlyout, StrongBox<double>> Hooked = [];

    private static readonly List<WeakReference<MenuFlyout>> Menus = [];

    /// <summary>Los menús de cada parte de una ventana, vivos mientras viva ella.</summary>
    private static readonly ConditionalWeakTable<DependencyObject, List<MenuFlyout>> Kept = [];

    private static readonly Dictionary<(string Glyph, string Pack, bool Dark, int Size, uint Text), WriteableBitmap> Cache = [];

    private static readonly List<WeakReference<SelectorBar>> Bars = [];

    /// <summary>La fuente que lleva un FontIcon al que no se le pone ninguna (la de los iconos de Windows 11).</summary>
    private static string DefaultFont => _defaultFont ??= new FontIcon().FontFamily?.Source ?? "";

    private static string? _defaultFont;

    static ThemedIcons()
    {
        IconPacks.Changed += Refresh;
        ThemeManager.Changed += _ => Refresh();
    }

    /// <summary>
    /// Busca en la ventana los menús (de botones y de clic derecho) y las pestañas, y los deja con los iconos del
    /// conjunto. Los menús se completan al abrirse (así valen también los elementos que se añaden por código).
    /// </summary>
    public static void Attach(DependencyObject root)
    {
        // Los menús de la ventana se guardan con ella (con la parte de la ventana que se busca, que la ventana guarda):
        // si .NET liberara su objeto, el menú seguiría en la ventana pero sin que nadie le cambiara los iconos.
        var kept = Kept.GetOrCreateValue(root);
        foreach (var element in Descendants(root))
        {
            if (element is Button { Flyout: MenuFlyout menu })
            {
                Hook(menu);
                if (!kept.Contains(menu))
                {
                    kept.Add(menu);
                }
            }

            if (element is UIElement { ContextFlyout: MenuFlyout context })
            {
                Hook(context);
                if (!kept.Contains(context))
                {
                    kept.Add(context);
                }
            }

            if (element is SelectorBar bar)
            {
                Bars.Add(new WeakReference<SelectorBar>(bar));
                Apply(bar);
            }
        }
    }

    /// <summary>Un menú creado por código: sus iconos se ponen con el conjunto cada vez que se abre.</summary>
    public static void Hook(MenuFlyout menu)
    {
        if (Hooked.TryGetValue(menu, out _))
        {
            return;
        }

        var scale = new StrongBox<double>(1);
        Hooked.Add(menu, scale);
        Menus.Add(new WeakReference<MenuFlyout>(menu));
        menu.Opening += (_, _) =>
        {
            scale.Value = menu.XamlRoot?.RasterizationScale ?? scale.Value;
            Apply(menu.Items, scale.Value);
        };
    }

    public static void Apply(IEnumerable<MenuFlyoutItemBase> items, double scale)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case MenuFlyoutSubItem sub:
                    sub.Icon = IconFor(sub, sub.Icon, scale, 16);
                    Apply(sub.Items, scale);
                    break;
                case MenuFlyoutItem menuItem:
                    menuItem.Icon = IconFor(menuItem, menuItem.Icon, scale, 16);
                    if (menuItem.Icon is ImageIcon image)
                    {
                        image.Opacity = menuItem.IsEnabled ? 1 : 0.4; // como el texto de una opción desactivada
                    }

                    break;
            }
        }
    }

    private static void Apply(SelectorBar bar)
    {
        double scale = bar.XamlRoot?.RasterizationScale ?? 1;
        foreach (var item in bar.Items)
        {
            item.Icon = IconFor(item, item.Icon, scale, 16);
        }
    }

    private static void Refresh()
    {
        Cache.Clear();

        // Los menús también, ya: si no, la primera vez que se abrían tras cambiar de conjunto podían salir con los iconos
        // del anterior.
        foreach (var weak in Menus.ToList())
        {
            if (weak.TryGetTarget(out var menu) && Hooked.TryGetValue(menu, out var scale))
            {
                Apply(menu.Items, scale.Value);
            }
            else
            {
                Menus.Remove(weak);
            }
        }

        foreach (var weak in Bars.ToList())
        {
            if (weak.TryGetTarget(out var bar))
            {
                Apply(bar);
            }
            else
            {
                Bars.Remove(weak);
            }
        }
    }

    /// <summary>
    /// El icono que toca: el de la fuente con el conjunto de Windows; si no, dibujado con el conjunto (si ya era una
    /// imagen, la misma con el dibujo nuevo).
    /// </summary>
    private static IconElement? IconFor(DependencyObject owner, IconElement? current, double scale, double size)
    {
        if (owner.GetValue(OriginalGlyphProperty) is not string glyph)
        {
            if (current is not FontIcon { Glyph: { Length: > 0 } original })
            {
                return current; // sin icono, o uno que no es de fuente: se deja
            }

            glyph = original;
            owner.SetValue(OriginalGlyphProperty, glyph);
        }

        var pack = IconPacks.Current;
        if (pack.Style == IconStyle.Mono)
        {
            // Windows 11: la letra de la fuente de siempre. Windows 10: la misma letra, de su fuente (o de la de Windows 11
            // si esa no la tiene). Si el icono ya es ese, se deja: uno nuevo en un menú ya abierto alguna vez no se pinta.
            string font = pack.FontFamily == IconPacks.FluentFont ? "" : IconPacks.FontFor(glyph, pack);
            if (current is FontIcon kept && kept.FontFamily?.Source == (font.Length > 0 ? font : DefaultFont))
            {
                return kept;
            }

            var icon = new FontIcon { Glyph = glyph };
            if (font.Length > 0)
            {
                icon.FontFamily = new FontFamily(font);
            }

            return icon;
        }

        int pixels = Math.Max(12, (int)Math.Round(size * scale));
        var text = TextColor();
        var key = (glyph, pack.Id, ThemeManager.Current.Dark, pixels, (uint)((text.A << 24) | (text.R << 16) | (text.G << 8) | text.B));
        if (!Cache.TryGetValue(key, out var bitmap))
        {
            bitmap = new WriteableBitmap(pixels, pixels);
            var data = IconPainter.ToPremultipliedBgra(IconPainter.Render(glyph, pack, pixels, text));
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(data);
            }

            bitmap.Invalidate();
            Cache[key] = bitmap;
        }

        if (current is ImageIcon same)
        {
            same.Source = bitmap;
            return same;
        }

        return new ImageIcon { Source = bitmap, Width = size, Height = size };
    }

    private static Color TextColor() => ThemeManager.Current.Dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0x1A, 0x1A, 0x1A);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                pending.Push(VisualTreeHelper.GetChild(current, i));
            }
        }
    }
}
