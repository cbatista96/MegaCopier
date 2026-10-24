using MegaCopier.App.Controls;
using MegaCopier.App.Services;
using MegaCopier.Engine.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

public sealed partial class SettingsPage
{
    private InfoBadge? _updatesBadge;
    private Button _updateAction = null!;
    private Button _updateCancel = null!;
    private TextBlock _updateTitle = null!, _updateVersion = null!, _updateDetail = null!;
    private ProgressBar _updateProgress = null!;
    private InfoBar _updateMessage = null!;
    private ReleaseNotesView _releaseNotes = null!;
    private StackPanel _updateHistory = null!, _updateLanding = null!, _updateVersionPage = null!;
    private ReleaseNotesView _historyNotes = null!;
    private string _historyVersion = "";
    private string _historyMarkdown = "", _historyTag = "";
    private bool _manualCurrentResult;
    private bool _manualNetworkError, _manualDownloadError;
    private int _updateVisit;
    private TextBlock _updateNotesTitle = null!;
    private UpdateStatusIcon _updateIcon = null!;
    private int _updateView;
    private DispatcherTimer? _updateTimer;
    private string? _shownNotes, _historyLanguage;
    private bool _updateLoading;

    private static TextBlock UpdateText(string key, string style = "McBody") => new()
    { Text = L.Get(key), TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources[style] };
    private static Border UpdateCard(UIElement child) => new() { Style = (Style)Application.Current.Resources["McSettingCard"], Child = child };

    private void BuildUpdatesSection()
    {
        UpdatesBreadcrumb.LayoutUpdated += (_, _) => AlignBreadcrumbChevrons();
        SearchBox.SizeChanged += (_, _) => AlignUpdateHeader();
        var content = new StackPanel { Spacing = 0 };
        _updateLanding = content;
        var status = new StackPanel { Spacing = 12 };
        var header = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _updateIcon = new UpdateStatusIcon(); header.Children.Add(_updateIcon);
        var labels = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        _updateTitle = UpdateText("Updates_Unknown", "McTitle");
        _updateVersion = UpdateText("Section_Updates", "McSecondary");
        _updateDetail = UpdateText("Updates_SearchDescription", "McSecondary");
        _updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        // Una sola acción, con tamaño independiente del texto y de la fase.
        _updateAction = new Button { Width = 300, Height = 44, HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)Application.Current.Resources["McAccentButton"] };
        _updateAction.Content = new TextBlock { Text = L.Get("Updates_Search"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        AutomationProperties.SetAutomationId(_updateAction, "UpdateAction");
        _updateAction.Click += UpdateAction_Click;
        Grid.SetColumn(labels, 1); header.Children.Add(labels);
        labels.Children.Add(_updateTitle); labels.Children.Add(_updateVersion); labels.Children.Add(_updateDetail);
        Grid.SetColumn(_updateAction, 2); header.Children.Add(_updateAction);
        header.SizeChanged += (_, e) =>
        {
            bool compact = e.NewSize.Width < 680;
            Grid.SetColumnSpan(labels, compact ? 2 : 1);
            Grid.SetRow(_updateAction, compact ? 1 : 0);
            Grid.SetColumn(_updateAction, compact ? 0 : 2);
            Grid.SetColumnSpan(_updateAction, compact ? 3 : 1);
        };
        _updateCancel = new Button { Content = L.Get("Updates_Cancel"), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["McButton"] };
        _updateCancel.Click += (_, _) => _app.Updates?.CancelDownload();
        _updateProgress.Height = 4;
        status.Children.Add(header); status.Children.Add(_updateProgress); status.Children.Add(_updateCancel);
        content.Children.Add(UpdateCard(status));
        _updateMessage = new InfoBar { IsClosable = true, Severity = InfoBarSeverity.Warning, IsOpen = false };
        content.Children.Add(_updateMessage);
        _releaseNotes = new ReleaseNotesView();
        var historyButton = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 4), Style = (Style)Application.Current.Resources["McButton"] };
        var historyRow = new Grid { ColumnSpacing = 12 };
        historyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        historyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        historyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        historyRow.Children.Add(new McIcon { Glyph = "\uE81C", FontSize = 20, Accent = true, VerticalAlignment = VerticalAlignment.Center });
        var historyLabel = UpdateText("Updates_History");
        historyLabel.VerticalAlignment = VerticalAlignment.Center; historyLabel.TextLineBounds = TextLineBounds.Tight;
        Grid.SetColumn(historyLabel, 1); historyRow.Children.Add(historyLabel);
        var chevron = new McIcon { Glyph = "\uE76C", FontSize = 14, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(chevron, 2); historyRow.Children.Add(chevron);
        historyButton.Content = historyRow;
        AutomationProperties.SetAutomationId(historyButton, "UpdateHistory");
        AutomationProperties.SetName(historyButton, L.Get("Updates_History"));
        historyButton.Click += (_, _) => { ShowUpdateHistory(); SetUpdateView(1); };
        _updateNotesTitle = UpdateText("Updates_CurrentHighlights", "McSection");
        content.Children.Add(_updateNotesTitle);
        var preview = new StackPanel { Spacing = 12 };
        preview.Children.Add(_releaseNotes);
        var fullNotes = new HyperlinkButton { Content = L.Get("Updates_ViewFullChangelog"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 4, 0, 4) };
        fullNotes.Click += (_, _) =>
        {
            if (_app.Updates is not { } updates) return;
            string version = updates.HasUpdate ? updates.Version : updates.Current.ToString();
            _historyVersion = "MegaCopier " + version;
            ShowHistoryNotes(updates.Notes, "v" + version.TrimStart('v'));
            ShowUpdateHistory();
            SetUpdateView(1, animate: false);
            SetUpdateView(2);
        };
        preview.Children.Add(fullNotes);
        content.Children.Add(UpdateCard(preview));
        content.Children.Add(historyButton);
        content.Children.Add(UpdateToggle("Updates_AutoCheck", "Updates_AutoCheckDescription", "\uE895",
            () => _app.Settings.CheckUpdatesAutomatically, value => _app.Settings.CheckUpdatesAutomatically = value));
        content.Children.Add(UpdateToggle("Updates_AutoDownload", "Updates_AutoDownloadDescription", "\uE896",
            () => _app.Settings.DownloadUpdatesAutomatically, value => _app.Settings.DownloadUpdatesAutomatically = value));
        content.Children.Add(UpdateToggle("Updates_AutoInstall", "Updates_AutoInstallDescription", "\uE823",
            () => _app.Settings.InstallUpdatesWhenIdle, value => _app.Settings.InstallUpdatesWhenIdle = value));
        var networks = new StackPanel { Spacing = 6 };
        networks.Children.Add(UpdateText("Updates_Networks"));
        var networkChoices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28 };
        networkChoices.Children.Add(UpdateCheck("Updates_Wifi", () => _app.Settings.UpdateAllowWifiEthernet, value => _app.Settings.UpdateAllowWifiEthernet = value));
        networkChoices.Children.Add(UpdateCheck("Updates_Mobile", () => _app.Settings.UpdateAllowMobileMetered, value => _app.Settings.UpdateAllowMobileMetered = value));
        networks.Children.Add(new ScrollViewer { Content = networkChoices, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        networks.Children.Add(UpdateText("Updates_NetworkDescription", "McSecondary"));
        content.Children.Add(UpdateCard(networks));
        var dismiss = new HyperlinkButton { Content = L.Get("Updates_Dismiss"), HorizontalAlignment = HorizontalAlignment.Left };
        dismiss.Click += (_, _) => _app.Updates?.Dismiss();
        content.Children.Add(dismiss);
        _updateHistory = new StackPanel { Spacing = 4 };
        _updateVersionPage = new StackPanel { Spacing = 14 };
        _historyNotes = new ReleaseNotesView(); _updateVersionPage.Children.Add(UpdateCard(_historyNotes));
        Sec_Updates.Children.Add(content); Sec_Updates.Children.Add(_updateHistory); Sec_Updates.Children.Add(_updateVersionPage);
        SetUpdateView(0);
        Loaded += UpdatesLoaded;
        Unloaded += (_, _) =>
        {
            _updateTimer?.Stop();
            if (_app.Updates is { } updates) updates.Changed -= RefreshUpdates;
        };
    }

    private Border UpdateToggle(string title, string description, string glyph, Func<bool> read, Action<bool> write)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new McIcon { Glyph = glyph, FontSize = 18, Accent = true, VerticalAlignment = VerticalAlignment.Center });
        var labels = new StackPanel(); labels.Children.Add(UpdateText(title)); labels.Children.Add(UpdateText(description, "McSecondary"));
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, L.Get(title));
        Grid.SetColumn(toggle, 2); row.Children.Add(toggle);
        toggle.Loaded += (_, _) => { _updateLoading = true; toggle.IsOn = read(); _updateLoading = false; };
        toggle.Toggled += (_, _) => { if (!_updateLoading) { write(toggle.IsOn); _app.Settings.Save(); _app.Updates?.RefreshNetworkPolicy(); RefreshUpdates(); } };
        return UpdateCard(row);
    }
    private CheckBox UpdateCheck(string title, Func<bool> read, Action<bool> write)
    {
        var box = new CheckBox { Content = new TextBlock { Text = L.Get(title), TextWrapping = TextWrapping.Wrap } };
        box.Loaded += (_, _) => box.IsChecked = read();
        box.Click += (_, _) => { write(box.IsChecked == true); _app.Settings.Save(); _app.Updates?.RefreshNetworkPolicy(); };
        return box;
    }

    private void UpdatesLoaded(object sender, RoutedEventArgs e)
    {
        if (_app.Updates is { } updates) { updates.Changed -= RefreshUpdates; updates.Changed += RefreshUpdates; }
        _updateTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _updateTimer.Tick -= UpdatesTick; _updateTimer.Tick += UpdatesTick; _updateTimer.Start();
        RefreshUpdates();
    }
    private void UpdatesTick(object? sender, object e) { if (Sec_Updates.Visibility == Visibility.Visible) RefreshUpdates(); }

    internal void OpenUpdates()
    {
        SearchBox.Text = "";
        SectionList.SelectedIndex = Sections.Length - 1;
        ShowSection(Sections.Length - 1);
        SetUpdateView(0);
        RefreshUpdates();
    }

    private void RefreshUpdates()
    {
        if (_app.Updates is not { } u) return;
        if (_updatesBadge is not null) _updatesBadge.Visibility = u.Badge ? Visibility.Visible : Visibility.Collapsed;
        var phase = u.Phase;
        if (!_manualNetworkError && (phase == UpdatePhase.Error && u.Error == UpdateError.Network
            || phase == UpdatePhase.WaitingForNetwork && !u.HasInternet)) phase = u.LastKnownPhase;
        bool offline = _manualNetworkError && !u.HasInternet;
        bool busy = phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Verifying or UpdatePhase.Installing;
        _updateIcon.Show(offline ? UpdateIconState.Offline : !_app.Settings.CheckUpdatesAutomatically ? UpdateIconState.Disabled
            : u.HasUpdate || phase == UpdatePhase.Error ? UpdateIconState.Attention
            : phase == UpdatePhase.Current ? UpdateIconState.Current : UpdateIconState.Unknown, busy && !offline);
        string action = phase switch
        {
            UpdatePhase.Checking => "Updates_Searching", UpdatePhase.Downloading => "Updates_Downloading",
            UpdatePhase.Verifying => "Updates_Verifying", UpdatePhase.Installing => "Updates_Installing",
            UpdatePhase.Ready => u.IsInstalled ? "Updates_Install" : "Updates_OpenZip",
            UpdatePhase.Current => _manualCurrentResult && !offline ? "Updates_CurrentAction" : "Updates_Search",
            _ => u.HasUpdate ? u.IsInstalled ? "Updates_Download" : "Updates_DownloadZip" : "Updates_Search",
        };
        ((TextBlock)_updateAction.Content).Text = L.Get(action);
        _updateNotesTitle.Text = L.Get(u.HasUpdate ? "Updates_AvailableHighlights" : "Updates_CurrentHighlights");
        _updateAction.IsEnabled = !busy && (phase != UpdatePhase.Ready || !u.IsInstalled || _app.CanUpdate);
        AutomationProperties.SetName(_updateAction, L.Get(action));
        _updateTitle.Text = L.Get(offline ? "Updates_NoInternet" : phase switch
        {
            UpdatePhase.Checking => "Updates_Searching", UpdatePhase.Current => "Updates_Current",
            UpdatePhase.Downloading => "Updates_Downloading", UpdatePhase.Verifying => "Updates_Verifying",
            UpdatePhase.Ready => "Updates_Ready", UpdatePhase.WaitingForNetwork => "Updates_WaitNetwork",
            UpdatePhase.Installing => "Updates_Installing", _ => u.HasUpdate ? "Updates_Available" : "Updates_Unknown",
        });
        _updateVersion.Text = u.HasUpdate ? L.F("Updates_Versions", u.Current, u.Version) : L.F("Updates_InstalledVersion", u.Current);
        _updateProgress.Visibility = busy || phase == UpdatePhase.Ready ? Visibility.Visible : Visibility.Collapsed;
        _updateProgress.IsIndeterminate = phase != UpdatePhase.Ready && (phase != UpdatePhase.Downloading || u.Progress.Total <= 0);
        _updateProgress.Value = phase == UpdatePhase.Ready ? 100 : u.Progress.Total > 0 ? Math.Clamp(100.0 * u.Progress.Received / u.Progress.Total, 0, 100) : 0;
        _updateCancel.Visibility = phase is UpdatePhase.Downloading or UpdatePhase.Verifying ? Visibility.Visible : Visibility.Collapsed;
        _updateDetail.Text = offline ? L.Get(_manualDownloadError ? "Updates_NoInternetDownload" : "Updates_NoInternetDescription") : phase switch
        {
            UpdatePhase.Downloading => L.F("Updates_Bytes", ViewModels.Format.Bytes(u.Progress.Received), ViewModels.Format.Bytes(u.Progress.Total)),
            UpdatePhase.Ready when u.IsInstalled && !_app.CanUpdate => L.Get("Updates_Busy"),
            UpdatePhase.Ready when u.IsInstalled => L.Get("Updates_InstallDescription"),
            _ when u.HasUpdate && !u.IsInstalled => L.Get(u.Portable ? "Updates_Portable" : "Updates_Uninstalled"),
            _ => L.Get("Updates_SearchDescription"),
        };
        _updateMessage.IsOpen = u.Error is not null && !offline && (u.Error != UpdateError.Network || _manualNetworkError);
        if (u.Error is { } error) _updateMessage.Message = L.Get("Updates_Error" + error);
        if (_shownNotes != u.Notes)
        {
            _shownNotes = u.Notes;
            _releaseNotes.Show(ReleaseNotes.Summary(u.Notes), u.HasUpdate ? u.Cache.Release!.Tag : "v" + u.Current);
        }
    }

    private void ShowUpdateHistory()
    {
        if (_app.Updates is not { } updates) return;
        string language = updates.Spanish ? "es" : "en";
        if (_historyLanguage == language) return;
        _historyLanguage = language;
        _updateHistory.Children.Clear();
        var versions = ReleaseNotes.History(updates.HistoryText, updates.Current);
        int shown = 0;
        var moreLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var more = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8),
            Style = (Style)Application.Current.Resources["McButton"],
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { moreLabel, new McIcon { Glyph = "\uE70D", FontSize = 12, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        void LoadNext()
        {
            foreach (var version in versions.Skip(shown).Take(10))
            {
                var stack = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                var title = UpdateText("Updates_History");
                title.FontSize = 16;
                title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                title.Text = "MegaCopier " + version.Version;
                stack.Children.Add(title);
                if (version.Version == updates.Current.ToString()) stack.Children.Add(UpdateText("Updates_InstalledMarker", "McSecondary"));
                if (version.Date.Length > 0) stack.Children.Add(new TextBlock { Text = version.Date, Style = (Style)Application.Current.Resources["McSecondary"] });
                var row = new Grid { ColumnSpacing = 16 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.Children.Add(stack);
                var action = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var label = UpdateText("Updates_ViewChangelog"); label.MaxWidth = 230; label.VerticalAlignment = VerticalAlignment.Center; label.TextLineBounds = TextLineBounds.Tight;
                label.Foreground = Theming.ThemeManager.AccentBrush; action.Children.Add(label);
                var arrow = new McIcon { Glyph = "\uE76C", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                action.Children.Add(arrow); Grid.SetColumn(action, 1); row.Children.Add(action);
                row.SizeChanged += (_, e) =>
                {
                    bool compact = e.NewSize.Width < 500;
                    row.RowSpacing = compact ? 8 : 0;
                    Grid.SetRow(action, compact ? 1 : 0); Grid.SetColumn(action, compact ? 0 : 1); Grid.SetColumnSpan(action, compact ? 2 : 1);
                };
                var button = new Button { Content = row, Padding = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["McButton"] };
                AutomationProperties.SetName(button, "MegaCopier " + version.Version + " · " + L.Get("Updates_ViewChangelog"));
                button.Click += (_, _) =>
                {
                    _historyVersion = "MegaCopier " + version.Version;
                    ShowHistoryNotes(version.Notes, "v" + version.Version);
                    SetUpdateView(2);
                };
                _updateHistory.Children.Insert(shown++, button);
            }
            moreLabel.Text = L.F("Gallery_More", Math.Min(10, versions.Count - shown));
            more.Visibility = shown < versions.Count ? Visibility.Visible : Visibility.Collapsed;
        }
        more.Click += (_, _) => LoadNext();
        _updateHistory.Children.Add(more);
        LoadNext();
    }

    private void ShowHistoryNotes(string markdown, string tag)
    {
        _historyMarkdown = markdown; _historyTag = tag; _historyNotes.Show(markdown, tag);
    }

    private void AlignUpdateHeader()
    {
        if (Sec_Updates.Visibility != Visibility.Visible || UpdatesBreadcrumb.Visibility != Visibility.Visible) return;
        SectionHeader.Margin = new Thickness(SectionHeader.Margin.Left, 6, SectionHeader.Margin.Right, 10);
    }

    private void AlignBreadcrumbChevrons()
    {
        // El glifo de Segoe Fluent Icons tiene el centro óptico dos píxeles por encima
        // del texto de 26 puntos. Compensar solo los separadores de la plantilla nativa,
        // manteniendo intactas la altura de la ruta y las áreas de pulsación.
        foreach (var text in Responsive.Descendants<TextBlock>(UpdatesBreadcrumb))
            if (text.Name == "PART_ChevronTextBlock" && text.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform)
                text.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { Y = 2 };
    }

    private void SetUpdateView(int view, bool animate = true)
    {
        int previous = _updateView;
        _updateView = view;
        _updateLanding.Visibility = view == 0 ? Visibility.Visible : Visibility.Collapsed;
        _updateHistory.Visibility = view == 1 ? Visibility.Visible : Visibility.Collapsed;
        _updateVersionPage.Visibility = view == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (Sec_Updates.Visibility == Visibility.Visible)
        {
            SectionTitle.Text = L.Get("Section_Updates");
            SectionTitle.Visibility = view == 0 ? Visibility.Visible : Visibility.Collapsed;
            SectionTitle.TextLineBounds = TextLineBounds.Full;
            if (view == 0) SectionHeader.ClearValue(HeightProperty);
            else SectionHeader.Height = 44;
            UpdatesBreadcrumb.Visibility = view == 0 ? Visibility.Collapsed : Visibility.Visible;
            AlignUpdateHeader();
            UpdatesBreadcrumb.ItemsSource = view == 0 ? new string[] { L.Get("Section_Updates") } : view == 1
                ? new string[] { L.Get("Section_Updates"), L.Get("Updates_History") }
                : new string[] { L.Get("Section_Updates"), L.Get("Updates_History"), _historyVersion };
            SectionScroll.ChangeView(null, 0, null, disableAnimation: true);
            if (previous != view && IsLoaded && animate && !_restoringSettings)
            {
                var page = view == 0 ? _updateLanding : view == 1 ? _updateHistory : _updateVersionPage;
                Motion.SlideX(page, view > previous ? 60 : -60, 0, 280, null, decelerate: true);
            }
            NavigationChanged?.Invoke();
        }
    }

    private void UpdatesBreadcrumb_Clicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index < 0 || args.Index >= _updateView) return;
        if (args.Index == 1) ShowUpdateHistory();
        SetUpdateView(args.Index);
    }

    private async void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Updates is not { } updates) return;
        int visit = _updateVisit;
        _manualNetworkError = false;
        _manualDownloadError = updates.HasUpdate && updates.Phase != UpdatePhase.Ready;
        try
        {
            if (updates.Phase == UpdatePhase.Ready) await updates.InstallAsync();
            else if (updates.HasUpdate)
            {
                await updates.RefreshConnectionAsync();
                if (!updates.HasInternet) { await updates.DownloadAsync(); return; }
                bool once = false;
                if (!updates.CanUseNetwork)
                {
                    var dialog = new ContentDialog { Title = L.Get("Updates_RestrictedTitle"), Content = L.Get("Updates_RestrictedBody"),
                        PrimaryButtonText = L.Get("Updates_AllowOnce"), CloseButtonText = L.Get("Connection_Cancel"),
                        Style = (Style)Application.Current.Resources["McContentDialogStyle"], XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
                    once = await dialog.ShowAsync() == ContentDialogResult.Primary;
                    if (!once) return;
                }
                await updates.DownloadAsync(allowOnce: once);
            }
            else
            {
                _manualCurrentResult = false;
                await updates.CheckAsync();
                _manualCurrentResult = visit == _updateVisit && updates.Phase == UpdatePhase.Current;
                RefreshUpdates();
            }
        }
        catch (Exception ex) { App.LogCrash(ex, "Actualizaciones: botón"); }
        finally
        {
            if (visit == _updateVisit) _manualNetworkError = updates.Error == UpdateError.Network;
            RefreshUpdates();
        }
    }
}
