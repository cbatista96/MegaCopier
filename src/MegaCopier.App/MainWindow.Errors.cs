using MegaCopier.App.Services;
using MegaCopier.Engine;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine.Jobs;
using Microsoft.UI.Xaml;
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

    /// <summary>¿Sumar los archivos nuevos a la copia que ya está en marcha?.</summary>
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
