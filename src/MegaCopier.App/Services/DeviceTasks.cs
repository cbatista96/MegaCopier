using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>
/// Tareas «al conectar el disco» (como Bvckup 2 o GoodSync): cada pocos segundos mira qué discos hay y, cuando aparece
/// uno con el número de serie de una tarea, la lanza (con sus rutas pasadas a la letra que tenga ahora). Los discos que
/// ya estaban al arrancar MegaCopier no cuentan: hay que conectarlos.
/// </summary>
internal sealed class DeviceTasks
{
    private readonly App _app;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private HashSet<string> _present = [];
    private bool _started;

    public DeviceTasks(App app)
    {
        _app = app;
        _timer = app.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Tick += (_, _) => _ = CheckAsync();
    }

    public void Start()
    {
        if (!_started)
        {
            _started = true;
            _ = CheckAsync(); // primera foto de los discos: no lanza nada
            _timer.Start();
        }
    }

    private bool _checking;

    private async Task CheckAsync()
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            // Leer los discos puede tardar (uno que se duerme): fuera del hilo de la interfaz.
            var now = await Task.Run(Serials);
            var arrived = now.Except(_present).ToList();
            bool first = _present.Count == 0 && !_seen;
            _present = now;
            _seen = true;
            if (first || arrived.Count == 0)
            {
                return;
            }

            foreach (var (path, serial) in await Task.Run(Pending))
            {
                if (arrived.Contains(serial) && !Running(path))
                {
                    App.TestLog("device-tasks.log", $"disco {serial} conectado: {Path.GetFileNameWithoutExtension(path)}");
                    _app.RunList(path);
                }
            }
        }
        finally
        {
            _checking = false;
        }
    }

    private bool _seen;

    private static HashSet<string> Serials()
    {
        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is DriveType.Removable or DriveType.Fixed && drive.IsReady
                    && Volumes.SerialOf(drive.RootDirectory.FullName) is { } serial)
                {
                    serials.Add(serial);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return serials;
    }

    /// <summary>Las tareas guardadas que esperan a un disco: (lista, número de serie).</summary>
    private static List<(string Path, string Serial)> Pending() =>
        [.. ScheduledTasks.List()
            .Select(ScheduledTasks.PathOf)
            .Select(path => (Path: path, Record: Reports.LoadList(path)))
            .Where(t => t.Record is { StartOnVolume.Length: > 0 })
            .Select(t => (t.Path, t.Record!.StartOnVolume))];

    /// <summary>Ya hay una copia de esa tarea en marcha (no se lanza dos veces si el disco parpadea).</summary>
    private bool Running(string path)
    {
        if (Reports.LoadList(path) is not { } list)
        {
            return true;
        }

        return _app.Queue.Jobs.Any(j => j.IsActive
            && j.Record.Sources.SequenceEqual(list.Sources, StringComparer.OrdinalIgnoreCase)
            && string.Equals(j.Record.Destination, list.Destination, StringComparison.OrdinalIgnoreCase));
    }
}
