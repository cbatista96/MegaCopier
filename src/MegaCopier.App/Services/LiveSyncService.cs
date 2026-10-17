using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Sync;

namespace MegaCopier.App.Services;

/// <summary>Una carpeta sincronizada al momento (se guarda en Ajustes).</summary>
public sealed class LiveSyncPair
{
    public string Source { get; set; } = "";

    /// <summary>Carpeta donde vive la copia (queda Destino\nombre-del-origen).</summary>
    public string Destination { get; set; } = "";

    /// <summary>Lo que se borra del origen se borra también de la copia.</summary>
    public bool Mirror { get; set; }
}

/// <summary>
/// Mantiene vigiladas las carpetas de "Sincronización en tiempo real" mientras MegaCopier está abierto. Cada pasada
/// usa las opciones de Ajustes (verificar, límite de velocidad...) y va por su cuenta, sin llenar la lista de copias;
/// su estado se ve en Ajustes y, si algo falla, avisa (una vez, hasta que vuelva a ir bien).
/// </summary>
internal sealed class LiveSyncService(App app) : IDisposable
{
    private readonly Dictionary<LiveSyncPair, LiveSync> _watchers = [];
    private readonly Dictionary<LiveSyncPair, string> _invalid = [];
    private readonly HashSet<LiveSyncPair> _warned = [];

    /// <summary>Cambió el estado de alguna carpeta (en el hilo de la interfaz).</summary>
    public event Action? StatusChanged;

    /// <summary>Vuelve a vigilar según Ajustes (al abrir y al cambiar la lista).</summary>
    public void Apply()
    {
        Stop();
        foreach (var pair in app.Settings.LiveSyncs)
        {
            if (LiveSync.Validate(pair.Source, pair.Destination) is { } problem)
            {
                _invalid[pair] = problem;
                continue;
            }

            try
            {
                var sync = new LiveSync(pair.Source, pair.Destination, pair.Mirror, () => app.Settings.NewJobOptions(new IpcRequest { Destination = pair.Destination }));
                sync.Synced += (_, result) => app.Dispatcher.TryEnqueue(() => OnSynced(pair, result));
                _watchers[pair] = sync;
                sync.Start();
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                _invalid[pair] = ex.Message;
            }
        }

        StatusChanged?.Invoke();
    }

    /// <summary>Añade una carpeta a sincronizar y empieza; si no se puede, devuelve por qué.</summary>
    public string? Add(string source, string destination, bool mirror = false)
    {
        if (LiveSync.Validate(source, destination) is { } problem)
        {
            return problem;
        }

        app.Settings.LiveSyncs.Add(new LiveSyncPair { Source = source, Destination = destination, Mirror = mirror });
        app.Settings.Save();
        Apply();
        return null;
    }

    public void SyncNow(LiveSyncPair pair)
    {
        if (_watchers.TryGetValue(pair, out var sync))
        {
            sync.SyncNow();
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Texto de estado para Ajustes y si es un problema.</summary>
    public (string Text, bool Problem) Status(LiveSyncPair pair)
    {
        if (_invalid.TryGetValue(pair, out string? problem))
        {
            return (L.F("LiveSync_Invalid", problem), true);
        }

        if (!_watchers.TryGetValue(pair, out var sync))
        {
            return ("", false);
        }

        if (sync.IsSyncing)
        {
            return (L.Get("LiveSync_Syncing"), false);
        }

        return sync.Last switch
        {
            null => (L.Get("LiveSync_Waiting"), false),
            { Error: { } error } last => (L.F("LiveSync_Error", ViewModels.Format.Date(last.AtUtc), error), true),
            var last => (L.F("LiveSync_Last", ViewModels.Format.Date(last.AtUtc), last.Copied, last.Deleted, last.Failed), last.Failed > 0),
        };
    }

    /// <summary>Dónde queda la copia de esta carpeta.</summary>
    public static string Target(LiveSyncPair pair) =>
        Path.Combine(pair.Destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(pair.Source)));

    private void OnSynced(LiveSyncPair pair, LiveSyncResult result)
    {
        if (App.TestMode)
        {
            App.TestLog("livesync.log", $"{pair.Source} copiados={result.Copied} quitados={result.Deleted} errores={result.Failed} {result.Error}");
        }

        bool problem = result.Error is not null || result.Failed > 0;
        if (problem && _warned.Add(pair))
        {
            app.Notify(L.Get("LiveSync_FailedTitle"), result.Error is { } error
                ? L.F("LiveSync_FailedError", pair.Source, error)
                : L.F("LiveSync_FailedFiles", pair.Source, result.Failed));
        }
        else if (!problem)
        {
            _warned.Remove(pair);
        }

        StatusChanged?.Invoke();
    }

    private void Stop()
    {
        foreach (var sync in _watchers.Values)
        {
            sync.Dispose();
        }

        _watchers.Clear();
        _invalid.Clear();
    }

    public void Dispose() => Stop();
}
