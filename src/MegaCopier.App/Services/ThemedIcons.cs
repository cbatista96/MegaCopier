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
    /// <summary>El glifo original de cada elemento (el FontIcon que tenía en el XAML).</summary>
    private static readonly ConditionalWeakTable<DependencyObject, string> Glyphs = [];

    private static readonly ConditionalWeakTable<MenuFlyout, object> Hooked = [];

    private static readonly Dictionary<(string Glyph, string Pack, bool Dark, int Size, uint Text), WriteableBitmap> Cache = [];

    private static readonly List<WeakReference<SelectorBar>> Bars = [];

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
        foreach (var element in Descendants(root))
        {
            if (element is Button { Flyout: MenuFlyout menu })
            {
                Hook(menu);
            }

            if (element is UIElement { ContextFlyout: MenuFlyout context })
            {
                Hook(context);
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

        Hooked.Add(menu, true);
        menu.Opening += (_, _) => Apply(menu.Items, menu.XamlRoot?.RasterizationScale ?? 1);
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

    /// <summary>El icono que toca: el de la fuente con el conjunto de Windows; si no, dibujado con el conjunto.</summary>
    private static IconElement? IconFor(DependencyObject owner, IconElement? current, double scale, double size)
    {
        if (!Glyphs.TryGetValue(owner, out string? glyph))
        {
            if (current is not FontIcon { Glyph: { Length: > 0 } original })
            {
                return current; // sin icono, o uno que no es de fuente: se deja
            }

            glyph = original;
            Glyphs.Add(owner, glyph);
        }

        var pack = IconPacks.Current;
        if (pack.Style == IconStyle.Mono && pack.FontFamily == IconPacks.FluentFont)
        {
            return current is FontIcon ? current : new FontIcon { Glyph = glyph };
        }

        if (pack.Style == IconStyle.Mono)
        {
            // Windows 10: la misma letra, de su fuente (o de la de Windows 11 si esa no la tiene).
            return new FontIcon { Glyph = glyph, FontFamily = new FontFamily(IconPacks.FontFor(glyph, pack)) };
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
