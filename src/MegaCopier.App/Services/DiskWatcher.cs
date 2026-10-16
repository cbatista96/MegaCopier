using MegaCopier.Engine;

namespace MegaCopier.App.Services;

/// <summary>
/// Vigila la salud de los discos (SMART, según Windows): al empezar una copia que lee o escribe en un disco que avisa de
/// fallo, lo dice una vez (mejor sacar los datos de ahí cuanto antes).
/// </summary>
internal sealed class DiskWatcher(App app)
{
    private readonly HashSet<int> _warned = [];
    private List<DiskStatus> _disks = [];
    private DateTime _lastQuery = DateTime.MinValue;
    private bool _querying;

    public IReadOnlyList<DiskStatus> Disks => _disks;

    public event Action? Changed;

    /// <summary>Consulta en segundo plano (como mucho cada 30 minutos, salvo que se fuerce).</summary>
    public void Refresh(bool force = false)
    {
        if (_querying || (!force && DateTime.UtcNow - _lastQuery < TimeSpan.FromMinutes(30)))
        {
            return;
        }

        _querying = true;
        _ = Task.Run(() =>
        {
            var disks = DiskHealth.Query();
            app.Dispatcher.TryEnqueue(() =>
            {
                _disks = disks;
                _lastQuery = DateTime.UtcNow;
                _querying = false;
                Changed?.Invoke();
                if (App.TestMode)
                {
                    App.TestLog("disks.log", string.Join(" | ", disks.Select(d => $"{d.Number}:{d.Name}:{d.Health}")));
                }
            });
        });
    }

    /// <summary>Al poner una copia en marcha: ¿toca algún disco que avisa de fallo?</summary>
    public void Check(IEnumerable<string> paths)
    {
        Refresh();
        foreach (int disk in paths.Select(DiskHealth.DiskOf).OfType<int>().Distinct())
        {
            if (_disks.FirstOrDefault(d => d.Number == disk) is { IsBad: true } bad && _warned.Add(disk))
            {
                app.Notify(L.Get("Disk_WarningTitle"), L.F("Disk_Warning", bad.Name, bad.Number, bad.Detail.Length > 0 ? bad.Detail : L.Get("Disk_HealthWarning")));
                if (App.TestMode)
                {
                    App.TestLog("disks.log", $"aviso: disco {bad.Number} {bad.Name} {bad.Detail}");
                }
            }
        }
    }
}
