using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using MegaCopier.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App;

/// <summary>
/// Comprobar un archivo contra una suma copiada (la que publica la web de una descarga): se elige el archivo, la suma
/// se toma del portapapeles si hay una, y MegaCopier dice si coincide y con qué algoritmo (lo deduce por su longitud).
/// </summary>
public sealed partial class MainWindow
{
    private async void CompareSum_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFileAsync() is not { } file)
        {
            return;
        }

        string clipboard = "";
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                clipboard = await content.GetTextAsync();
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Portapapeles ocupado por otro programa: se escribe a mano.
        }

        var sumBox = new TextBox
        {
            Header = L.Get("SumCheck_Header"),
            PlaceholderText = L.Get("SumCheck_Placeholder"),
            Text = HashMatch.Extract(clipboard) ?? "",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
        };
        var bar = new ProgressBar { Maximum = 1, Visibility = Visibility.Collapsed };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var panel = new StackPanel { Spacing = 10, MinWidth = 420, MaxWidth = 520 };
        panel.Children.Add(new TextBlock { Text = L.F("SumCheck_File", Path.GetFileName(file)), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(sumBox);
        panel.Children.Add(bar);
        panel.Children.Add(status);

        var dialog = new ContentDialog
        {
            Title = L.Get("SumCheck_Title"),
            Content = panel,
            PrimaryButtonText = L.Get("SumCheck_Check"),
            CloseButtonText = L.Get("SumCheck_Close"),
            DefaultButton = ContentDialogButton.Primary,
            Style = (Style)Application.Current.Resources["McContentDialogStyle"],
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.RequestedTheme,
        };

        using var cancel = new CancellationTokenSource();
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; // el diálogo sigue abierto para ver el resultado
            if (HashMatch.Extract(sumBox.Text) is not { } expected)
            {
                status.Text = L.Get("SumCheck_NotASum");
                status.Foreground = ThemeManager.ErrorBrush;
                return;
            }

            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            sumBox.IsEnabled = false;
            bar.Value = 0;
            bar.Visibility = Visibility.Visible;
            status.ClearValue(TextBlock.ForegroundProperty);
            status.Text = L.Get("SumCheck_Reading");
            try
            {
                var progress = new Progress<double>(value => bar.Value = value);
                var result = await Task.Run(() => HashMatch.Check(file, expected, progress, cancel.Token));
                string algorithm = AlgorithmName(result.Algorithm);
                status.Text = result.Matches ? L.F("SumCheck_Match", algorithm) : L.F("SumCheck_NoMatch", algorithm, result.Actual);
                status.Foreground = result.Matches ? ThemeManager.SuccessBrush : ThemeManager.ErrorBrush;
                Sounds.Play(_app.Settings.Sounds, result.Matches ? SoundEvent.Done : SoundEvent.Errors);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                status.Text = ex.Message;
                status.Foreground = ThemeManager.ErrorBrush;
            }
            finally
            {
                bar.Visibility = Visibility.Collapsed;
                sumBox.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };

        await _dialogGate.WaitAsync();
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            cancel.Cancel(); // cerrar a mitad deja de leer el archivo
            _dialogGate.Release();
        }
    }

    private static string AlgorithmName(FileHashAlgorithm algorithm) => algorithm switch
    {
        FileHashAlgorithm.Crc32 => "CRC32",
        FileHashAlgorithm.XxHash3 => "xxHash3",
        FileHashAlgorithm.XxHash64 => "xxHash64",
        FileHashAlgorithm.XxHash128 => "xxHash128",
        FileHashAlgorithm.Md5 => "MD5",
        FileHashAlgorithm.Sha1 => "SHA-1",
        FileHashAlgorithm.Sha256 => "SHA-256",
        FileHashAlgorithm.Sha512 => "SHA-512",
        FileHashAlgorithm.Sha3_256 => "SHA3-256",
        FileHashAlgorithm.Sha3_512 => "SHA3-512",
        FileHashAlgorithm.Blake3 => "BLAKE3",
        _ => algorithm.ToString(),
    };
}
