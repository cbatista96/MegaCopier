using System.Reflection;
using MegaCopier.App.Services;
using MegaCopier.Engine.Updates;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MegaCopier.App.Controls;

/// <summary>Notas completas como texto nativo seleccionable. El documento nunca se interpreta como HTML o XAML.</summary>
internal sealed partial class ReleaseNotesView : StackPanel
{
    private readonly List<Grid> _imageRows = [];
    public ReleaseNotesView()
    {
        Spacing = 10;
        SizeChanged += (_, _) =>
        {
            foreach (var row in _imageRows) row.Width = Math.Min(row.MaxWidth, ActualWidth);
        };
    }

    public void Show(string markdown, string tag)
    {
        Children.Clear();
        _imageRows.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            Children.Add(new TextBlock { Text = L.Get("Updates_NotesUnavailable"), TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["McSecondary"] });
            return;
        }
        Grid? imageRow = null;
        int imageGroup = 0;
        foreach (var block in ReleaseNotes.Parse(markdown))
        {
            if (block.Kind != NoteKind.Image) { imageRow = null; imageGroup = 0; }
            if (block.Kind == NoteKind.Rule)
            {
                Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6), Background = (Brush)Application.Current.Resources["McBorderBrush"] });
                continue;
            }
            if (block.Kind == NoteKind.Image)
            {
                int width = block.ImageWidth > 0 ? Math.Min(860, block.ImageWidth) : 860;
                var holder = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
                var image = new Image { MaxWidth = width, MaxHeight = 720, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, block.Text);
                holder.Children.Add(image);
                var fallback = new TextBlock { Text = block.Text, Style = (Style)Application.Current.Resources["McSecondary"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
                holder.Children.Add(fallback);
                image.Loaded += async (_, _) =>
                {
                    try
                    {
                        byte[]? bytes = await UpdateNoteImages.ReadAsync(block.Url!, tag);
                        if (bytes is null) { fallback.Visibility = Visibility.Visible; return; }
                        using var stream = new MemoryStream(bytes);
                        using var random = stream.AsRandomAccessStream();
                        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(random);
                        if (decoder.PixelWidth > 8192 || decoder.PixelHeight > 8192) { fallback.Visibility = Visibility.Visible; return; }
                        random.Seek(0);
                        var bitmap = new BitmapImage { DecodePixelWidth = 900 };
                        await bitmap.SetSourceAsync(random);
                        image.Source = bitmap;
                    }
                    catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException or ArgumentException) { fallback.Visibility = Visibility.Visible; }
                };
                if (block.ImageGroup == 0) { imageRow = null; imageGroup = 0; Children.Add(holder); }
                else
                {
                    if (imageRow is null || imageGroup != block.ImageGroup)
                    {
                        imageRow = new Grid { ColumnSpacing = 12, RowSpacing = 10, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 0 };
                        imageGroup = block.ImageGroup; Children.Add(imageRow); _imageRows.Add(imageRow);
                        imageRow.SizeChanged += (_, e) => LayoutImages((Grid)holder.Parent, e.NewSize.Width);
                    }
                    imageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
                    imageRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetColumn(holder, imageRow.Children.Count); imageRow.Children.Add(holder);
                    imageRow.MaxWidth += width + (imageRow.Children.Count > 1 ? 12 : 0);
                    imageRow.Width = Math.Min(imageRow.MaxWidth, ActualWidth > 0 ? ActualWidth : imageRow.MaxWidth);
                }
                continue;
            }
            var text = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["McTextBrush"] };
            var paragraph = new Paragraph();
            if (block.Kind == NoteKind.Heading)
            {
                text.FontSize = block.Level <= 2 ? 20 : 15;
                text.FontWeight = FontWeights.SemiBold;
                text.Margin = new Thickness(0, 10, 0, 0);
            }
            if (block.Kind == NoteKind.Code) text.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            if (block.Kind == NoteKind.ListItem)
            {
                text.Margin = new Thickness(12 + block.Level * 14, 0, 0, 0);
                paragraph.Inlines.Add(new Run { Text = "•  " });
            }
            foreach (var part in block.Kind == NoteKind.Code ? [new NoteInline(block.Text, Code: true)] : ReleaseNotes.Inline(block.Text))
            {
                var run = new Run { Text = part.Text };
                if (part.Bold) run.FontWeight = FontWeights.SemiBold;
                if (part.Italic) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
                if (part.Code) run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                if (part.Url is not null && UpdateNoteImages.Resolve(part.Url, tag) is { Scheme: "https" } uri)
                {
                    var link = new Hyperlink();
                    link.Inlines.Add(run);
                    link.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(uri);
                    paragraph.Inlines.Add(link);
                }
                else paragraph.Inlines.Add(run);
            }
            text.Blocks.Add(paragraph);
            Children.Add(text);
        }
    }

    private static void LayoutImages(Grid row, double width)
    {
        bool narrow = width < 560;
        for (int i = 0; i < row.Children.Count; i++)
        {
            Grid.SetRow((FrameworkElement)row.Children[i], narrow ? i : 0);
            Grid.SetColumn((FrameworkElement)row.Children[i], narrow ? 0 : i);
            Grid.SetColumnSpan((FrameworkElement)row.Children[i], narrow ? row.ColumnDefinitions.Count : 1);
        }
    }
}
