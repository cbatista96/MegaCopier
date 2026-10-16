using System.Collections.Concurrent;
using System.Globalization;
using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>
/// Línea de tiempo de la cola: cuándo empezará y terminará cada copia, con las mismas reglas que la cola (en el mismo
/// disco, de una en una) y la velocidad de cada disco (medida en esta copia, por el autoajuste o la de otras copias).
/// </summary>
public sealed partial class TimelinePage : UserControl
{
    /// <summary>Velocidad supuesta de un disco del que no se sabe nada (un disco mecánico normal).</summary>
    private const double DefaultBytesPerSecond = 150 * 1024 * 1024;

    private readonly Dictionary<string, Row> _rows = [];
    private readonly ConcurrentDictionary<string, long> _sizes = new();
    private readonly HashSet<string> _measuring = [];
    private readonly Dictionary<string, double> _observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueueTimer _timer;
    private IReadOnlyList<JobViewModel> _jobs = [];

    public TimelinePage()
    {
        InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Refresh();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        });
    }

    public event Action? CloseRequested;

    /// <summary>Se eligió una copia de la línea de tiempo (para verla).</summary>
    public event Action<JobViewModel>? JobChosen;

    public void Show(IReadOnlyList<JobViewModel> jobs)
    {
        _jobs = jobs;
        Refresh();
    }

    /// <summary>Recalcula la previsión y actualiza las filas (sin rehacerlas, para que no parpadeen).</summary>
    public void Refresh()
    {
        var queue = ((App)Application.Current).Queue;
        var now = DateTime.UtcNow;
        var active = _jobs.Where(j => j.IsActive).ToList();
        var inputs = active.Select(Input).ToList();
        var entries = QueueTimeline.Estimate(inputs, queue.FastDeviceKeys, queue.MaxActiveJobs, now).ToDictionary(e => e.Id);

        foreach (string gone in _rows.Keys.Where(id => !entries.ContainsKey(id)).ToList())
        {
            Rows.Children.Remove(_rows[gone].Root);
            _rows.Remove(gone);
        }

        Empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Axis.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (entries.Count == 0)
        {
            Summary.Text = "";
            return;
        }

        var end = entries.Values.Max(e => e.EndUtc);
        if (end - now < TimeSpan.FromMinutes(1))
        {
            end = now + TimeSpan.FromMinutes(1);
        }

        double span = (end - now).TotalSeconds;
        Summary.Text = L.F("Timeline_Summary", When(end), Format.Duration(end - now));
        Tick0.Text = L.Get("Timeline_Now");
        Tick1.Text = Clock(now.AddSeconds(span / 4));
        Tick2.Text = Clock(now.AddSeconds(span / 2));
        Tick3.Text = Clock(now.AddSeconds(span * 3 / 4));
        Tick4.Text = Clock(end);

        // En el orden en que van a empezar.
        int index = 0;
        foreach (var job in active.Where(j => entries.ContainsKey(j.Job.Id)).OrderBy(j => entries[j.Job.Id].StartUtc))
        {
            var entry = entries[job.Job.Id];
            if (!_rows.TryGetValue(entry.Id, out var row))
            {
                row = new Row(job, j => JobChosen?.Invoke(j));
                _rows[entry.Id] = row;
                Rows.Children.Insert(Math.Min(index, Rows.Children.Count), row.Root);
            }
            else if (Rows.Children.IndexOf(row.Root) != index)
            {
                Rows.Children.Remove(row.Root);
                Rows.Children.Insert(Math.Min(index, Rows.Children.Count), row.Root);
            }

            row.Update(job, entry, now, span);
            index++;
        }

        if (TextAudit.Enabled)
        {
            // Cuando los textos nuevos ya están colocados (justo después de cambiarlos aún miden lo de antes).
            var audit = DispatcherQueue.CreateTimer();
            audit.Interval = TimeSpan.FromMilliseconds(400);
            audit.IsRepeating = false;
            audit.Tick += (_, _) => TextAudit.Run(this, $"Línea de tiempo {(int)ActualWidth}px");
            audit.Start();
        }
    }

    private TimelineInput Input(JobViewModel vm)
    {
        var job = vm.Job;
        bool running = vm.IsRunning || vm.IsPaused;
        long? remaining = null;
        double speed = 0;
        if (job.Progress is { TotalBytes: > 0 } p && p.Phase != CopyPhase.Preparing)
        {
            remaining = p.TotalBytes - p.CompletedBytes;
            if (p.BytesPerSecond > 0 && vm.IsRunning)
            {
                speed = p.BytesPerSecond;
                foreach (string device in job.Devices)
                {
                    _observed[device] = speed;
                }
            }
        }
        else if (job.Record.Operation == JobOperation.Delete && !job.Record.SecureDelete)
        {
            remaining = 0; // borrar no mueve datos: es casi inmediato
        }
        else if (_sizes.TryGetValue(job.Id, out long size))
        {
            remaining = size;
        }
        else
        {
            Measure(job);
        }

        if (speed <= 0)
        {
            speed = Expected(job);
        }

        DateTime? started = job.StartedAt is { } at ? at.ToUniversalTime() : null;
        return new TimelineInput(job.Id, job.Devices, running, started, running ? null : job.Record.StartAtUtc, remaining, speed);
    }

    /// <summary>Velocidad prevista: la vista en esta sesión en esos discos, la del autoajuste, o una normal.</summary>
    private double Expected(CopyJob job)
    {
        var seen = job.Devices.Where(_observed.ContainsKey).Select(d => _observed[d]).ToList();
        if (seen.Count > 0)
        {
            return seen.Min(); // el disco más lento manda
        }

        string target = job.Record.Operation is JobOperation.Copy or JobOperation.Decrypt ? job.Record.Destination : job.Record.Sources.FirstOrDefault() ?? "";
        return target.Length > 0 && DeviceTuner.ProfileFor(target) is { MBps: > 0 } profile ? profile.MBps * 1024 * 1024 : DefaultBytesPerSecond;
    }

    private void Measure(CopyJob job)
    {
        if (!_measuring.Add(job.Id))
        {
            return;
        }

        var sources = job.Record.Sources.ToList();
        _ = Task.Run(() =>
        {
            _sizes[job.Id] = QueueTimeline.MeasureSources(sources);
            DispatcherQueue.TryEnqueue(Refresh);
        });
    }

    /// <summary>Con horas cercanas (menos de 10 minutos) también los segundos: si no, todo diría la misma hora.</summary>
    private static string Clock(DateTime utc) => utc.ToLocalTime().ToString(Math.Abs((utc - DateTime.UtcNow).TotalMinutes) < 10 ? "T" : "t", CultureInfo.CurrentCulture);

    /// <summary>Hora (y "mañana" o la fecha, si no es hoy).</summary>
    private static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        string time = Clock(utc);
        return local.Date == DateTime.Today ? time
            : local.Date == DateTime.Today.AddDays(1) ? L.F("Timeline_Tomorrow", time)
            : local.ToString("g", CultureInfo.CurrentCulture);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    /// <summary>Una copia: nombre, barra proporcional en el eje y cuándo empieza y termina.</summary>
    private sealed class Row
    {
        private readonly TextBlock _name = new() { Style = (Style)Application.Current.Resources["McStrong"], TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _detail = new() { Style = (Style)Application.Current.Resources["McSecondary"], TextWrapping = TextWrapping.Wrap };
        private readonly Grid _track = new() { Height = 12, CornerRadius = new CornerRadius(6), Background = ThemeManager.TrackBrush };
        private readonly Border _fill = new() { CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
        private double _start;
        private double _end;
        private JobViewModel _job;

        public Row(JobViewModel job, Action<JobViewModel> chosen)
        {
            _job = job;
            _track.Children.Add(_fill);
            _track.SizeChanged += (_, _) => Place();

            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(_name);
            content.Children.Add(_track);
            content.Children.Add(_detail);
            Root = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 8, 10, 8),
                Background = ThemeManager.SurfaceBrush,
                BorderBrush = ThemeManager.BorderBrush,
            };
            Root.Click += (_, _) => chosen(_job);
        }

        public Button Root { get; }

        public void Update(JobViewModel job, TimelineEntry entry, DateTime now, double span)
        {
            _job = job;
            _name.Text = job.Title;
            ToolTipService.SetToolTip(_name, job.Title);

            _start = Math.Clamp((entry.StartUtc - now).TotalSeconds / span, 0, 1);
            _end = Math.Clamp((entry.EndUtc - now).TotalSeconds / span, _start, 1);
            Place();

            _fill.Background = job.IsPaused ? ThemeManager.WarningBrush : ThemeManager.AccentBrush;
            _fill.Opacity = job.IsRunning ? 1 : entry.Known ? 0.55 : 0.3;

            var length = entry.EndUtc - entry.StartUtc;
            string text = job.IsRunning ? L.F("Timeline_Running", When(entry.EndUtc), Format.Duration(entry.EndUtc - now))
                : job.IsPaused ? L.F("Timeline_Paused", When(entry.EndUtc))
                : L.F(job.IsScheduled ? "Timeline_Scheduled" : "Timeline_Queued", When(entry.StartUtc), When(entry.EndUtc), Format.Duration(length));
            _detail.Text = entry.Known ? text : text + " · " + L.Get("Timeline_Measuring");
            AutomationProperties.SetName(Root, $"{job.Title}. {_detail.Text}");
        }

        /// <summary>
        /// La barra en su sitio del eje. Con una copia programada para dentro de horas, las de unos segundos serían
        /// invisibles: tienen un ancho mínimo y nunca se salen por la derecha.
        /// </summary>
        private void Place()
        {
            double width = _track.ActualWidth;
            if (width <= 0)
            {
                return;
            }

            double bar = Math.Min(width, Math.Max(12, (_end - _start) * width));
            _fill.Width = bar;
            _fill.Margin = new Thickness(Math.Min(_start * width, width - bar), 0, 0, 0);
        }
    }
}
