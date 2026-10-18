using MegaCopier.App.Services;
using MegaCopier.Engine;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine.Jobs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App;

/// <summary>Preguntas durante la copia: qué hacer con un archivo que sigue fallando y si sumar una copia a otra.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Un archivo sigue fallando tras los reintentos (con «Al ocurrir un error: preguntar»). Se llama desde el hilo de
    /// copia y lo bloquea hasta que el usuario responde, como los conflictos.
    /// </summary>
    internal ErrorDecision AskError(CopyJob job, ErrorInfo error, CancellationToken cancellation)
    {
        _dialogGate.Wait(cancellation);
        try
        {
            var answer = new TaskCompletionSource<ErrorDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    ShowJobs();
                    Select(Jobs.FirstOrDefault(j => j.Job == job) ?? _selected);
                    ShowAndActivate();
                    Sounds.Play(_app.Settings.Sounds, SoundEvent.Attention);
                    var skipAll = new CheckBox { Content = L.Get("ErrorAsk_SkipAll") };
                    var stop = new HyperlinkButton { Content = L.Get("ErrorAsk_Stop"), Padding = new Thickness(0) };
                    var dialog = new ContentDialog
                    {
                        Title = L.Get("ErrorAsk_Title"),
                        PrimaryButtonText = L.Get("ErrorAsk_Retry"),
                        SecondaryButtonText = L.Get("ErrorAsk_Skip"),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = Root.XamlRoot,
                        RequestedTheme = Root.RequestedTheme,
                        Content = new StackPanel
                        {
                            Spacing = 10,
                            MaxWidth = 460,
                            Children =
                            {
                                new TextBlock { Text = Path.GetFileName(error.Source), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                                new TextBlock { Text = error.Kind == FileErrorKind.Other ? error.Message : $"{L.Get("Error_" + error.Kind)} ({error.Message})", TextWrapping = TextWrapping.Wrap },
                                new TextBlock { Text = error.Source, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                                skipAll,
                                stop,
                            },
                        },
                    };
                    bool stopped = false;
                    stop.Click += (_, _) =>
                    {
                        stopped = true;
                        dialog.Hide();
                    };
                    using var registration = cancellation.Register(() => _dispatcher.TryEnqueue(dialog.Hide));
                    var result = await dialog.ShowAsync();
                    answer.TrySetResult(
                        stopped ? ErrorDecision.Stop
                        : result == ContentDialogResult.Primary ? ErrorDecision.Retry
                        : skipAll.IsChecked == true ? ErrorDecision.SkipAll
                        : ErrorDecision.Skip);
                }
                catch (Exception ex)
                {
                    answer.TrySetException(ex);
                }
            });

            return answer.Task.WaitAsync(cancellation).GetAwaiter().GetResult();
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    /// <summary>
    /// Una carpeta de origen ya existe en el destino con algunos de sus archivos: se pregunta una vez, antes de copiar
    /// nada. Cada opción es un botón grande con su icono y una frase que explica qué va a pasar.
    /// </summary>
    internal FolderDecision AskFolder(CopyJob job, FolderConflictInfo info, CancellationToken cancellation)
    {
        _dialogGate.Wait(cancellation);
        try
        {
            var answer = new TaskCompletionSource<FolderDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    ShowJobs();
                    Select(Jobs.FirstOrDefault(j => j.Job == job) ?? _selected);
                    ShowAndActivate();
                    Sounds.Play(_app.Settings.Sounds, SoundEvent.Attention);
                    string name = Path.GetFileName(info.Destination);
                    string parent = Format.ShortPath(Path.GetDirectoryName(info.Destination) ?? "", 3);
                    var dialog = new ContentDialog
                    {
                        Title = info.Folders == 1 ? L.F("FolderAsk_Title", name) : L.F("FolderAsk_TitleMany", info.Folders),
                        CloseButtonText = L.Get("FolderAsk_Cancel"),
                        XamlRoot = Root.XamlRoot,
                        RequestedTheme = Root.RequestedTheme,
                    };
                    var decision = FolderDecision.Cancel;
                    var options = new StackPanel { Spacing = 8 };
                    void Option(FolderDecision value, int glyph, string title, string text, bool recommended = false)
                    {
                        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        heading.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                        if (recommended)
                        {
                            heading.Children.Add(new Border
                            {
                                Padding = new Thickness(6, 1, 6, 2),
                                CornerRadius = new CornerRadius(4),
                                VerticalAlignment = VerticalAlignment.Center,
                                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["McAccentBrush"],
                                Child = new TextBlock
                                {
                                    Text = L.Get("FolderAsk_Recommended"),
                                    FontSize = 11,
                                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["McAccentTextBrush"],
                                },
                            });
                        }

                        var body = new Grid { ColumnSpacing = 14 };
                        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        body.Children.Add(new Controls.McIcon { Glyph = ((char)glyph).ToString(), FontSize = 20, Accent = true, VerticalAlignment = VerticalAlignment.Center });
                        var texts = new StackPanel { Spacing = 2 };
                        texts.Children.Add(heading);
                        texts.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 });
                        Grid.SetColumn(texts, 1);
                        body.Children.Add(texts);
                        var button = new Button
                        {
                            Content = body,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            Padding = new Thickness(14, 10, 14, 10),
                            CornerRadius = new CornerRadius(8),
                        };
                        AutomationProperties.SetName(button, title);
                        button.Click += (_, _) =>
                        {
                            decision = value;
                            dialog.Hide();
                        };
                        options.Children.Add(button);
                    }

                    Option(FolderDecision.SkipExisting, 0xE893, L.Get("FolderAsk_Skip"), L.Get("FolderAsk_SkipText"), recommended: true);
                    Option(FolderDecision.Overwrite, 0xE72C, L.Get("FolderAsk_Overwrite"), L.Get("FolderAsk_OverwriteText"));
                    Option(FolderDecision.Rename, 0xE8AC, L.Get("FolderAsk_Rename"), L.F("FolderAsk_RenameText", name + " (2)"));
                    Option(FolderDecision.AskEach, 0xE8FD, L.Get("FolderAsk_AskEach"), L.Get("FolderAsk_AskEachText"));
                    dialog.Content = new ScrollViewer
                    {
                        Content = new StackPanel
                        {
                            Spacing = 14,
                            MaxWidth = 480,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = L.F("FolderAsk_Message", parent, info.ExistingFiles, info.Files, Format.Bytes(info.ExistingBytes)),
                                    TextWrapping = TextWrapping.Wrap,
                                },
                                options,
                            },
                        },
                    };
                    using var registration = cancellation.Register(() => _dispatcher.TryEnqueue(dialog.Hide));
                    await dialog.ShowAsync();
                    answer.TrySetResult(decision);
                }
                catch (Exception ex)
                {
                    answer.TrySetException(ex);
                }
            });

            return answer.Task.WaitAsync(cancellation).GetAwaiter().GetResult();
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    /// <summary>¿Sumar los archivos nuevos a la copia que ya está en marcha?</summary>
    internal async Task<bool> ConfirmMergeAsync(CopyJob target, int count)
    {
        await _dialogGate.WaitAsync();
        try
        {
            ShowAndActivate();
            var dialog = new ContentDialog
            {
                Title = L.Get("Merge_Title"),
                Content = new TextBlock
                {
                    Text = L.F("Merge_Message", count, Format.ShortPath(target.Record.Destination, 3)),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = L.Get("Merge_Add"),
                SecondaryButtonText = L.Get("Merge_Separate"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _dialogGate.Release();
        }
    }
}
