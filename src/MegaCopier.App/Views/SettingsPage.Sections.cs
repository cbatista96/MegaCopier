using System.Numerics;
using MegaCopier.App.Controls;
using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

namespace MegaCopier.App.Views;

/// <summary>
/// Ajustes por secciones, como la Configuración de Windows 11: lista de secciones a la izquierda (con sus iconos),
/// búsqueda de ajustes, animaciones al cambiar de sección y al pulsar una opción, y galería de conjuntos de iconos.
/// </summary>
public sealed partial class SettingsPage
{
    private static readonly (string Panel, string Title, string Glyph)[] Sections =
    [
        ("Sec_General", "Section_General", ""),
        ("Sec_Interface", "Section_Interface", ""),
        ("Sec_Sounds", "Section_Sounds", ""),
        ("Sec_Copy", "Section_Copy", ""),
        ("Sec_Behavior", "Section_Behavior", ""),
        ("Sec_Errors", "Section_Errors", ""),
        ("Sec_Performance", "Section_Performance", ""),
        ("Sec_Network", "Section_Network", ""),
        ("Sec_Automation", "Section_Automation", ""),
        ("Sec_Advanced", "Section_Advanced", ""),
    ];

    /// <summary>Iconos de muestra de cada conjunto en la galería.</summary>
    private static readonly string[] SampleGlyphs = ["", "", "", "", "", ""];

    /// <summary>La última sección vista (al volver a Ajustes se abre en la misma).</summary>
    private static int _lastSection;

    private Controls.SlidingIndicator? _sectionIndicator;
    private bool _sectionsReady;
    private bool _searching;
    private GalleryMore? _iconMore;
    private GalleryMore? _themeMore;

    private void BuildSections()
    {
        foreach (var (panel, title, glyph) in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Padding = new Thickness(2, 6, 2, 6) };
            row.Children.Add(new McIcon { Glyph = glyph, FontSize = 18, Accent = true, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = L.Get(title), VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["McBody"] });
            var item = new ListViewItem { Content = row, Tag = panel };
            AutomationProperties.SetName(item, L.Get(title));
            SectionList.Items.Add(item);
        }

        _sectionsReady = true;
        SectionList.SelectedIndex = _lastSection;
        Loaded += (_, _) => HookCardAnimations();
        SizeChanged += (_, e) => ApplyNavWidth(e.NewSize.Width);
    }

    /// <summary>En ventanas estrechas la lista de secciones se queda en iconos (como Windows).</summary>
    private void ApplyNavWidth(double width)
    {
        bool compact = width < 760;
        NavColumn.Width = new GridLength(compact ? 64 : 260);
        SearchBox.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in SectionList.Items.OfType<ListViewItem>())
        {
            if (item.Content is StackPanel { Children: [_, TextBlock text] })
            {
                text.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private void Section_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_sectionsReady || SectionList.SelectedIndex < 0)
        {
            return;
        }

        if (_searching)
        {
            _searching = false;
            SearchBox.Text = "";
            ShowAllCards();
        }

        ShowSection(SectionList.SelectedIndex);
        if (SectionList.SelectedItem is ListViewItem { Content: StackPanel { Children: [McIcon icon, ..] } })
        {
            Motion.Icon(icon);
        }
    }

    private void ShowSection(int index)
    {
        _lastSection = index;
        var (panel, title, glyph) = Sections[index];
        foreach (var (name, _, _) in Sections)
        {
            if (FindName(name) is StackPanel section)
            {
                section.Visibility = name == panel ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        SearchEmpty.Visibility = Visibility.Collapsed;
        SectionTitle.Text = L.Get(title);
        SectionIcon.Glyph = glyph;
        SectionIcon.Visibility = Visibility.Visible;
        SectionScroll.ChangeView(null, 0, null, disableAnimation: true);
        SlideIn(SectionsHost);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Búsqueda

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        string text = sender.Text.Trim();
        if (text.Length == 0)
        {
            _searching = false;
            ShowAllCards();
            ShowSection(_lastSection);
            SectionList.SelectedIndex = _lastSection;
            return;
        }

        _searching = true;
        SectionList.SelectedIndex = -1;
        int found = 0;
        foreach (var (name, _, _) in Sections)
        {
            if (FindName(name) is not StackPanel section)
            {
                continue;
            }

            int inSection = 0;
            foreach (var child in section.Children.OfType<FrameworkElement>())
            {
                bool match = child is Border && TextOf(child).Contains(text, StringComparison.CurrentCultureIgnoreCase);
                child.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                inSection += match ? 1 : 0;
            }

            section.Visibility = inSection > 0 ? Visibility.Visible : Visibility.Collapsed;
            found += inSection;
        }

        SectionTitle.Text = L.F("Settings_SearchResults", text);
        SectionIcon.Glyph = "";
        SearchEmpty.Text = L.F("Settings_SearchNone", text);
        SearchEmpty.Visibility = found == 0 ? Visibility.Visible : Visibility.Collapsed;
        SectionScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void ShowAllCards()
    {
        foreach (var (name, _, _) in Sections)
        {
            if (FindName(name) is StackPanel section)
            {
                foreach (var child in section.Children.OfType<FrameworkElement>())
                {
                    child.Visibility = Visibility.Visible;
                }
            }
        }

        // Paneles que solo se ven con su opción activada (correo, informe de errores...).
        Load();
    }

    /// <summary>Todo el texto visible de una tarjeta (títulos, descripciones, casillas y opciones).</summary>
    private static string TextOf(DependencyObject root)
    {
        var parts = new List<string>();
        foreach (var element in Responsive.Descendants<FrameworkElement>(root))
        {
            switch (element)
            {
                case TextBlock text:
                    parts.Add(text.Text);
                    break;
                case ContentControl { Content: string content }:
                    parts.Add(content);
                    break;
                case ComboBox combo:
                    parts.AddRange(combo.Items.OfType<ComboBoxItem>().Select(i => i.Content as string ?? ""));
                    break;
            }
        }

        return string.Join(" ", parts);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Animaciones

    /// <summary>Al pulsar una tarjeta, su icono da un pequeño salto (como las opciones animadas de Windows 11).</summary>
    private void HookCardAnimations()
    {
        var cardStyle = (Style)Application.Current.Resources["McSettingCard"];
        foreach (var card in Responsive.Descendants<Border>(SectionsHost).Where(b => b.Style == cardStyle && b.Tag is not "anim"))
        {
            card.Tag = "anim";
            card.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) =>
            {
                if (Responsive.Descendants<McIcon>(card).FirstOrDefault() is { } icon)
                {
                    Motion.Icon(icon);
                }
            }), handledEventsToo: true);
        }
    }

    /// <summary>La sección nueva entra deslizándose y apareciendo.</summary>
    private static void SlideIn(UIElement element) => Motion.FadeIn(element, 24);

    // ---------------------------------------------------------------------------------------------------------
    // Conjuntos de iconos

    private void LoadIconGallery()
    {
        if (_iconMore is null)
        {
            var items = new List<object>();
            foreach (var pack in IconPacks.All)
            {
                var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (string glyph in SampleGlyphs)
                {
                    icons.Children.Add(new McIcon { Glyph = glyph, FontSize = 18, PackOverride = pack.Id, Accent = true, VerticalAlignment = VerticalAlignment.Center });
                }

                var tile = new StackPanel { Width = 230, Spacing = 8, Padding = new Thickness(10) };
                tile.Children.Add(new Border
                {
                    Height = 56,
                    CornerRadius = new CornerRadius(8),
                    Background = ThemeManager.SurfaceBrush,
                    BorderBrush = ThemeManager.BorderBrush,
                    BorderThickness = new Thickness(1),
                    Child = icons,
                });
                tile.Children.Add(new TextBlock { Text = L.Get("IconPack_" + pack.Id), Style = (Style)Application.Current.Resources["McBody"] });
                var item = new GridViewItem { Content = tile, Tag = pack.Id };
                AutomationProperties.SetName(item, L.Get("IconPack_" + pack.Id));
                items.Add(item);
            }

            _iconMore = new GalleryMore(
                IconGallery,
                () => items.OfType<GridViewItem>().FirstOrDefault(i => (string)i.Tag == IconPacks.Current.Id),
                item =>
                {
                    bool was = _loading;
                    _loading = true;
                    IconGallery.SelectedItem = item;
                    _loading = was;
                });
            _iconMore.SetItems(items);
            return;
        }

        _iconMore.Refresh();
    }

    private void IconGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || IconGallery.SelectedItem is not GridViewItem { Tag: string id } || id == _app.Settings.IconPack)
        {
            return;
        }

        _app.Settings.IconPack = id;
        _app.Settings.Save();
        IconPacks.Apply(id);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Opciones nuevas con acción propia

    /// <summary>Las opciones de «Fondo de la ventana», en el orden de la lista.</summary>
    private static readonly WindowBackdrop[] BackdropChoices =
        [WindowBackdrop.Theme, WindowBackdrop.Mica, WindowBackdrop.MicaAlt, WindowBackdrop.AcrylicBase, WindowBackdrop.AcrylicThin];

    private void Backdrop_Changed(object sender, SelectionChangedEventArgs e)
    {
        Store();
        if (!_loading)
        {
            _app.ApplyTheme();
        }
    }

    private async void ErrorReportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            _app.Settings.ErrorReportFolder = folder;
            _app.Settings.ErrorReportInDestination = false;
            _app.Settings.Save();
            Load();
        }
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Engine.DataFolder.Path}\"") { UseShellExecute = true });

    /// <summary>Deja los ajustes como recién instalado (las copias, el historial y los temas propios se conservan).</summary>
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = L.Get("Reset_Title"),
            Content = new TextBlock { Text = L.Get("Reset_Message"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = L.Get("Reset_Yes"),
            CloseButtonText = L.Get("Reset_No"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _app.Settings.Frozen = true;
        new AppSettings().Save();
        _app.Restart();
    }
}
