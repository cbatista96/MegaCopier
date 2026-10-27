using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using MegaCopier.Engine.Remote;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>
/// Ajustes > Conexiones: los servidores guardados (FTP, FTPS, WebDAV, S3, SFTP). Cada uno se edita en una ventana cuyos
/// campos cambian con el tipo, con "Probar conexión". Las contraseñas van al Administrador de credenciales.
/// </summary>
public sealed partial class SettingsPage
{
    private static readonly (ConnectionKind Kind, string Name)[] Kinds =
    [
        (ConnectionKind.WebDav, "WebDAV"), (ConnectionKind.Sftp, "SFTP"), (ConnectionKind.Ftps, "FTPS (TLS)"),
        (ConnectionKind.Ftp, "FTP"), (ConnectionKind.S3, "S3"), (ConnectionKind.GoogleCloudStorage, "Google Cloud Storage"), (ConnectionKind.AzureBlob, "Azure Blob"),
        (ConnectionKind.OneDrive, "OneDrive"), (ConnectionKind.SharePoint, "SharePoint"), (ConnectionKind.GoogleDrive, "Google Drive"), (ConnectionKind.Dropbox, "Dropbox"),
    ];

    private void LoadConnections()
    {
        ConnectionList.Children.Clear();
        foreach (var connection in Connections.Load())
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeManager.AccentBrush });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = connection.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            string kind = Kinds.FirstOrDefault(k => k.Kind == connection.Kind).Name;
            text.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", new[] { kind, connection.Host, connection.User, connection.Root }.Where(s => !string.IsNullOrWhiteSpace(s))),
                FontSize = 12,
                Foreground = ThemeManager.TextSecondaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var captured = connection;
            var edit = IconButton("", L.F("Settings_ConnectionEdit", connection.Name));
            edit.Click += async (_, _) => await EditConnectionAsync(captured);
            Grid.SetColumn(edit, 2);
            row.Children.Add(edit);
            var remove = IconButton("", L.F("Settings_ConnectionDelete", connection.Name));
            remove.Click += (_, _) =>
            {
                Connections.Save([.. Connections.Load().Where(c => c.Id != captured.Id)]);
                Connections.DeleteSecret(captured.Id);
                LoadConnections();
            };
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
            ConnectionList.Children.Add(row);
        }

        if (ConnectionList.Children.Count == 0)
        {
            ConnectionList.Children.Add(new TextBlock { Text = L.Get("Settings_ConnectionsEmpty"), Foreground = ThemeManager.TextSecondaryBrush, FontSize = 12 });
        }
    }

    private async void ConnectionAdd_Click(object sender, RoutedEventArgs e) => await EditConnectionAsync(null);

    /// <summary>La ventana de una conexión (nueva si <paramref name="existing"/> es null).</summary>
    internal async Task EditConnectionAsync(Connection? existing)
    {
        var connection = existing ?? new Connection { Kind = ConnectionKind.WebDav };
        var kind = new ComboBox { Header = L.Get("Connection_Kind"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (_, kindName) in Kinds)
        {
            kind.Items.Add(kindName);
        }

        kind.SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, k => k.Kind == connection.Kind));
        var name = new TextBox { Header = L.Get("Connection_Name"), Text = connection.Name };
        var host = new TextBox { Text = connection.Host };
        var port = new NumberBox { Header = L.Get("Connection_Port"), Value = connection.Port, Minimum = 0, Maximum = 65535, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var user = new TextBox { Text = connection.User };
        var secret = new PasswordBox { Password = existing is null ? "" : Connections.GetSecret(existing.Id) };
        var root = new TextBox { Text = connection.Root };
        var region = new TextBox { Header = L.Get("Connection_Region"), Text = connection.Region };
        var acceptAny = new CheckBox { Content = L.Get("Connection_AcceptAny"), IsChecked = connection.AcceptAnyCertificate };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.TextSecondaryBrush };
        var test = new Button { Content = L.Get("Connection_Test") };
        var clientId = new TextBox { Header = L.Get("Connection_ClientId"), Text = connection.ClientId };
        var clientSecret = new TextBox { Header = L.Get("Connection_ClientSecret"), Text = connection.ClientSecret };
        var site = new TextBox { Header = L.Get("Connection_Site"), PlaceholderText = L.Get("Connection_SitePlaceholder"), Text = connection.Site };
        var cloudHelp = new TextBlock { Text = L.Get("Connection_CloudHelp"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = ThemeManager.TextSecondaryBrush };
        var signIn = new Button { Content = L.Get("Connection_SignIn") };
        string cloudToken = existing is not null && CloudAuth.IsCloud(existing.Kind) ? Connections.GetSecret(existing.Id) : "";

        // Los campos cambian con el tipo: dirección para WebDAV, bucket y región para S3, clave privada en SFTP...
        void Adapt()
        {
            var selected = Kinds[Math.Max(0, kind.SelectedIndex)].Kind;
            bool s3 = selected is ConnectionKind.S3 or ConnectionKind.GoogleCloudStorage; // Google Cloud Storage habla S3
            bool azure = selected == ConnectionKind.AzureBlob;
            host.Header = L.Get(selected switch
            {
                ConnectionKind.WebDav => "Connection_Url",
                ConnectionKind.AzureBlob => "Connection_AzureAccount",
                ConnectionKind.GoogleCloudStorage => "Connection_GcsEndpoint",
                ConnectionKind.S3 => "Connection_Endpoint",
                _ => "Connection_Host",
            });
            user.Header = L.Get(s3 ? "Connection_AccessKey" : "Connection_User");
            secret.Header = L.Get(selected switch
            {
                ConnectionKind.S3 or ConnectionKind.GoogleCloudStorage => "Connection_SecretKey",
                ConnectionKind.AzureBlob => "Connection_Sas",
                ConnectionKind.Sftp => "Connection_PasswordOrKey",
                _ => "Connection_Password",
            });
            root.Header = L.Get(s3 ? "Connection_Bucket" : azure ? "Connection_Container" : "Connection_Root");
            region.Visibility = s3 ? Visibility.Visible : Visibility.Collapsed;
            port.Visibility = selected is ConnectionKind.WebDav || s3 || azure ? Visibility.Collapsed : Visibility.Visible;
            acceptAny.Visibility = selected is ConnectionKind.Ftp or ConnectionKind.Sftp ? Visibility.Collapsed : Visibility.Visible;
            // Nubes: en lugar de servidor y contraseña, el identificador de la app y el inicio de sesión en el navegador.
            bool cloud = CloudAuth.IsCloud(selected);
            foreach (var control in new FrameworkElement[] { host, user, secret })
            {
                control.Visibility = cloud ? Visibility.Collapsed : Visibility.Visible;
            }

            if (azure)
            {
                user.Visibility = Visibility.Collapsed; // Azure: el permiso SAS ya lo dice todo
            }

            site.Visibility = selected == ConnectionKind.SharePoint ? Visibility.Visible : Visibility.Collapsed;

            if (cloud)
            {
                port.Visibility = region.Visibility = acceptAny.Visibility = Visibility.Collapsed;
                root.Header = L.Get("Connection_CloudRoot");
            }

            clientId.Visibility = signIn.Visibility = cloudHelp.Visibility = cloud ? Visibility.Visible : Visibility.Collapsed;
            clientSecret.Visibility = selected == ConnectionKind.GoogleDrive ? Visibility.Visible : Visibility.Collapsed;
        }

        Connection Read() => new()
        {
            Id = connection.Id,
            Name = name.Text.Trim() is { Length: > 0 } n ? n : host.Text.Trim(),
            Kind = Kinds[Math.Max(0, kind.SelectedIndex)].Kind,
            Host = host.Text.Trim(),
            Port = double.IsNaN(port.Value) ? 0 : (int)port.Value,
            User = user.Text.Trim(),
            Root = root.Text.Trim(),
            Region = region.Text.Trim(),
            AcceptAnyCertificate = acceptAny.IsChecked == true,
            HostKey = connection.HostKey,
            ClientId = clientId.Text.Trim(),
            ClientSecret = clientSecret.Text.Trim(),
            Site = site.Text.Trim(),
            AuthUrl = connection.AuthUrl,
            TokenUrl = connection.TokenUrl,
        };

        kind.SelectionChanged += (_, _) => Adapt();
        test.Click += async (_, _) =>
        {
            result.Text = L.Get("Connection_Testing");
            var candidate = Read();
            string password = CloudAuth.IsCloud(candidate.Kind) ? cloudToken : secret.Password;
            try
            {
                int count = await Task.Run(() =>
                {
                    using var store = Connections.Open(candidate, password);
                    return store.Children(store.RootId).Count;
                });
                connection.HostKey = candidate.HostKey; // SFTP: la huella aprendida al probar
                result.Text = L.F("Connection_TestOk", count);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException or NotSupportedException or System.Security.Authentication.AuthenticationException)
            {
                result.Text = L.F("Connection_TestFailed", ex.Message);
            }
        };
        signIn.Click += async (_, _) =>
        {
            var candidate = Read();
            if (candidate.ClientId.Length == 0)
            {
                result.Text = L.Get("Connection_NeedClientId");
                return;
            }

            result.Text = L.Get("Connection_SigningIn");
            try
            {
                cloudToken = await CloudAuth.SignInAsync(candidate, OpenBrowser);
                result.Text = L.Get("Connection_SignedIn");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
            {
                result.Text = L.F("Connection_TestFailed", ex.Message);
            }
        };
        Adapt();

        var panel = new StackPanel { Spacing = 10, MinWidth = 380, MaxWidth = 480 };
        foreach (var control in new UIElement[] { kind, name, host, port, user, secret, site, clientId, clientSecret, cloudHelp, signIn, root, region, acceptAny, test, result })
        {
            panel.Children.Add(control);
        }

        var dialog = new ContentDialog
        {
            Title = L.Get("Connection_DialogTitle"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = L.Get("Connection_Save"),
            CloseButtonText = L.Get("Connection_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var saved = Read();
        var all = Connections.Load();
        int index = all.FindIndex(c => c.Id == saved.Id);
        if (index >= 0)
        {
            all[index] = saved;
        }
        else
        {
            all.Add(saved);
        }

        Connections.Save(all);
        Connections.SetSecret(saved.Id, CloudAuth.IsCloud(saved.Kind) ? cloudToken : secret.Password);
        LoadConnections();
    }

    /// <summary>El inicio de sesión de una nube se hace en el navegador del usuario (en pruebas solo se anota la dirección).</summary>
    private static void OpenBrowser(string url)
    {
        if (App.TestMode)
        {
            App.TestLog("cloud.log", url);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
}
