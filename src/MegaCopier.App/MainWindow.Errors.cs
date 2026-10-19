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
    /// <summary>
    /// Pregunta dentro de la ventana pequeña: ocupa el sitio del panel de la copia (que no cambia de tamaño) con un
    /// título, una línea de detalle y una fila de botones con icono y texto corto; la explicación de cada opción sale
    /// al pasar el ratón. La primera opción es la recomendada; la última fila acaba en «Cancelar».
    /// </summary>
    private async Task<T> AskInlineAsync<T>(
        string glyph,
        string title,
        string text,
        IReadOnlyList<(T Value, string Glyph, string Label, string Tip)> options,
        (T Value, string Label, string Tip) cancel,
        CancellationToken cancellation)
    {
        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        QuestionIcon.Glyph = glyph;
        QuestionTitle.Text = title;
        QuestionText.Text = text;
        ToolTipService.SetToolTip(QuestionTitle, title);
        QuestionButtons.Children.Clear();
        QuestionButtons.ColumnDefinitions.Clear();

        Button Add(T value, string? icon, string label, string tip, string style)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            if (icon is not null)
            {
                content.Children.Add(new Controls.McIcon { Glyph = icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            }

            content.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = content,
                Style = (Style)Application.Current.Resources[style],
                MinWidth = 0,
                Padding = new Thickness(8, 6, 8, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(button, tip);
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, tip);
            button.Click += (_, _) => answer.TrySetResult(value);
            return button;
        }

        // Las opciones, a partes iguales en una fila; «Cancelar», arriba a la derecha (así las opciones caben enteras).
        for (int i = 0; i < options.Count; i++)
        {
            var (value, icon, label, tip) = options[i];
            var button = Add(value, icon, label, tip, i == 0 ? "McAccentButton" : "McButton");
            QuestionButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(button, i);
            QuestionButtons.Children.Add(button);
        }

        var cancelButton = Add(cancel.Value, "", cancel.Label, cancel.Tip, "McButton");
        cancelButton.HorizontalAlignment = HorizontalAlignment.Right;
        QuestionCancelHost.Content = cancelButton;

        PanelCard.Opacity = 0;
        PanelCard.IsHitTestVisible = false;
        QuestionCard.Visibility = Visibility.Visible;
        Motion.FadeIn(QuestionCard);
        using var registration = cancellation.Register(() => answer.TrySetResult(cancel.Value));
        try
        {
            return await answer.Task;
        }
        finally
        {
            QuestionCard.Visibility = Visibility.Collapsed;
            PanelCard.Opacity = 1;
            PanelCard.IsHitTestVisible = true;
            Motion.FadeIn(PanelCard);
        }
    }

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
                    if (!_expanded)
                    {
                        // Ventana pequeña: la pregunta cabe entera en ella, con las explicaciones al pasar el ratón.
                        answer.TrySetResult(await AskInlineAsync(
                            "",
                            info.Folders == 1 ? L.F("FolderAsk_Title", name) : L.F("FolderAsk_TitleMany", info.Folders),
                            L.F("FolderAsk_Short", info.ExistingFiles, info.Files, Format.Bytes(info.ExistingBytes), parent),
                            [
                                (FolderDecision.SkipExisting, "", L.Get("FolderAsk_SkipShort"), L.Get("FolderAsk_Skip") + "\n" + L.Get("FolderAsk_SkipText")),
                                (FolderDecision.Overwrite, "", L.Get("FolderAsk_Overwrite"), L.Get("FolderAsk_OverwriteText")),
                                (FolderDecision.Rename, "", L.Get("FolderAsk_Rename"), L.F("FolderAsk_RenameText", name + " (2)")),
                                (FolderDecision.AskEach, "", L.Get("FolderAsk_AskEachShort"), L.Get("FolderAsk_AskEach") + "\n" + L.Get("FolderAsk_AskEachText")),
                            ],
                            (FolderDecision.Cancel, L.Get("FolderAsk_CancelShort"), L.Get("FolderAsk_Cancel")),
                            cancellation));
                        return;
                    }

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

    /// <summary>
    /// Lo que se va a copiar no cabe en el destino (antes de empezar). Se llama desde el hilo de copia y lo bloquea
    /// hasta que el usuario responde: true = forzar la copia.
    /// </summary>
    internal bool AskSpace(CopyJob job, SpaceShortage shortage, CancellationToken cancellation)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                answer.TrySetResult(await AskSpaceAsync(job, shortage, adding: false, cancellation));
            }
            catch (Exception ex)
            {
                answer.TrySetException(ex);
            }
        });

        try
        {
            return answer.Task.WaitAsync(cancellation).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// «No hay espacio suficiente»: cuánto hace falta y cuánto hay libre, con dos salidas. Cancelar (no se copia, o no
    /// se añade, nada) y Forzar la copia (copia hasta llenar el disco y entonces espera: al liberar espacio, sigue).
    /// En la ventana pequeña la pregunta cabe dentro de ella; en la completa es un diálogo.
    /// </summary>
    internal async Task<bool> AskSpaceAsync(CopyJob job, SpaceShortage shortage, bool adding, CancellationToken cancellation = default)
    {
        await _dialogGate.WaitAsync(cancellation);
        try
        {
            ShowJobs();
            Select(Jobs.FirstOrDefault(j => j.Job == job) ?? _selected);
            ShowAndActivate();
            Sounds.Play(_app.Settings.Sounds, SoundEvent.Attention);
            string drive = DriveInfoText.Describe(shortage.Destination).Text;
            string title = L.F("Space_Title", drive);
            string text = L.F(adding ? "Space_TextAdding" : "Space_Text", Format.Bytes(shortage.Needed), Format.Bytes(shortage.Free), Format.Bytes(shortage.Needed - shortage.Free));
            if (!_expanded)
            {
                return await AskInlineAsync(
                    "",
                    title,
                    text,
                    [(true, "",L.Get("Space_Force"), L.Get("Space_ForceTip"))],
                    (false, L.Get("Space_Cancel"), L.Get(adding ? "Space_CancelAddingTip" : "Space_CancelTip")),
                    cancellation);
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = new StackPanel
                {
                    Spacing = 10,
                    MaxWidth = 460,
                    Children =
                    {
                        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = L.Get("Space_ForceTip"), TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 },
                    },
                },
                PrimaryButtonText = L.Get("Space_Force"),
                CloseButtonText = L.Get("Space_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme,
            };
            using var registration = cancellation.Register(() => _dispatcher.TryEnqueue(dialog.Hide));
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
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
