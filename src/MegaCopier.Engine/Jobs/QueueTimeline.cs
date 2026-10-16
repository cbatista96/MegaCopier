namespace MegaCopier.Engine.Jobs;

/// <summary>Lo que hace falta saber de un trabajo para situarlo en la línea de tiempo.</summary>
/// <param name="Running">En marcha o en pausa (ocupa sus discos).</param>
/// <param name="StartAtUtc">Programado para esa hora (no empieza antes).</param>
/// <param name="RemainingBytes">Lo que le falta por copiar (null = aún no se sabe).</param>
/// <param name="BytesPerSecond">Velocidad medida o prevista.</param>
public sealed record TimelineInput(
    string Id,
    IReadOnlySet<string> Devices,
    bool Running,
    DateTime? StartedUtc,
    DateTime? StartAtUtc,
    long? RemainingBytes,
    double BytesPerSecond);

/// <summary>Cuándo empezará y terminará un trabajo según la previsión.</summary>
/// <param name="Known">false si su tamaño aún no se sabe (la duración es una suposición).</param>
public sealed record TimelineEntry(string Id, DateTime StartUtc, DateTime EndUtc, bool Known);

/// <summary>
/// Previsión de la cola: cuándo empieza y termina cada trabajo, con las mismas reglas que la cola (uno a la vez por
/// disco salvo los rápidos, como mucho N trabajos, los programados no antes de su hora). La duración de cada trabajo es
/// lo que le falta entre su velocidad.
/// </summary>
public static class QueueTimeline
{
    /// <summary>Duración supuesta de un trabajo cuyo tamaño aún no se conoce.</summary>
    public static readonly TimeSpan UnknownDuration = TimeSpan.FromMinutes(1);

    public static List<TimelineEntry> Estimate(IReadOnlyList<TimelineInput> jobs, IReadOnlySet<string> fastDevices, int maxActiveJobs, DateTime nowUtc)
    {
        var entries = new Dictionary<string, TimelineEntry>();
        var active = new List<(TimelineInput Job, DateTime End)>();
        foreach (var job in jobs.Where(j => j.Running))
        {
            var end = nowUtc + Duration(job, out bool known);
            entries[job.Id] = new TimelineEntry(job.Id, job.StartedUtc ?? nowUtc, end, known);
            active.Add((job, end));
        }

        var waiting = jobs.Where(j => !j.Running).ToList();
        var time = nowUtc;
        while (waiting.Count > 0)
        {
            // Lo que haya terminado a esta hora deja libres sus discos.
            active.RemoveAll(a => a.End <= time);
            var busy = new HashSet<string>(active.SelectMany(a => a.Job.Devices), StringComparer.OrdinalIgnoreCase);
            busy.ExceptWith(fastDevices);
            int count = active.Count;

            // Igual que CopyQueue.Schedule: en orden, saltando lo que espera disco u hora.
            foreach (var job in waiting.ToList())
            {
                if (job.StartAtUtc is { } at && at > time)
                {
                    continue;
                }

                if (maxActiveJobs > 0 && count >= maxActiveJobs)
                {
                    break;
                }

                if (job.Devices.Overlaps(busy))
                {
                    continue;
                }

                var end = time + Duration(job, out bool known);
                entries[job.Id] = new TimelineEntry(job.Id, time, end, known);
                active.Add((job, end));
                busy.UnionWith(job.Devices.Except(fastDevices, StringComparer.OrdinalIgnoreCase));
                waiting.Remove(job);
                count++;
            }

            if (waiting.Count == 0)
            {
                break;
            }

            // Siguiente momento en que algo cambia: termina un trabajo o llega la hora de uno programado.
            var next = active.Select(a => a.End)
                .Concat(waiting.Where(w => w.StartAtUtc > time).Select(w => w.StartAtUtc!.Value))
                .Where(t => t > time)
                .DefaultIfEmpty(DateTime.MaxValue)
                .Min();
            if (next == DateTime.MaxValue)
            {
                break; // no debería pasar: nada lo bloquea y aun así no arranca
            }

            time = next;
        }

        return [.. jobs.Where(j => entries.ContainsKey(j.Id)).Select(j => entries[j.Id])];
    }

    /// <summary>Tamaño de unos orígenes (archivos y carpetas, recursivo), para prever los trabajos que aún no empezaron.</summary>
    public static long MeasureSources(IEnumerable<string> sources, CancellationToken cancellationToken = default)
    {
        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (string source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(source))
                {
                    total += new FileInfo(source).Length;
                }
                else if (Directory.Exists(source))
                {
                    foreach (var file in new DirectoryInfo(source).EnumerateFiles("*", options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        total += file.Length;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    private static TimeSpan Duration(TimelineInput job, out bool known)
    {
        known = job.RemainingBytes is not null && job.BytesPerSecond > 0;
        return known ? TimeSpan.FromSeconds(Math.Max(0, job.RemainingBytes!.Value) / job.BytesPerSecond) : UnknownDuration;
    }
}
