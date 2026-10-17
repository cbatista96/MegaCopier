using System.Collections.Concurrent;
using MegaCopier.Engine.Sync;

namespace MegaCopier.Engine.Tests;

public sealed class LiveSyncTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly BlockingCollection<LiveSyncResult> _passes = [];
    private LiveSync? _sync;

    public void Dispose()
    {
        _sync?.Dispose();
        _temp.Dispose();
    }

    private string Source => _temp.Combine("Fotos");

    private string Target => _temp.Combine("copia", "Fotos");

    private void Start(bool mirror)
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(_temp.Combine("copia"));
        _sync = new LiveSync(Source, _temp.Combine("copia"), mirror, () => new CopyOptions { ProgressInterval = TimeSpan.Zero }, TimeSpan.FromMilliseconds(300));
        _sync.Synced += (_, result) => _passes.Add(result);
        _sync.Start();
        Next(); // la primera pasada completa
    }

    private LiveSyncResult Next()
    {
        Assert.True(_passes.TryTake(out var result, TimeSpan.FromSeconds(20)), "no hubo pasada de sincronización");
        Assert.Null(result.Error);
        return result;
    }

    /// <summary>Espera hasta que se cumpla la condición (las pasadas llegan cuando el origen se calma).</summary>
    private void WaitFor(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "la copia no se puso al día");
            _passes.TryTake(out _, TimeSpan.FromMilliseconds(200));
        }
    }

    [Fact]
    public void CopiesNewAndChangedFilesAsTheyHappen()
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_temp.Combine("Fotos", "viaje")).FullName, "a.jpg"), "antes");
        Start(mirror: false);
        Assert.Equal("antes", File.ReadAllText(Path.Combine(Target, "viaje", "a.jpg")));

        File.WriteAllText(Path.Combine(Source, "nueva.jpg"), "nueva");
        WaitFor(() => File.Exists(Path.Combine(Target, "nueva.jpg")));

        File.WriteAllText(Path.Combine(Source, "viaje", "a.jpg"), "después, más largo");
        WaitFor(() => File.ReadAllText(Path.Combine(Target, "viaje", "a.jpg")) == "después, más largo");

        // Sin espejo, borrar en el origen no borra la copia.
        File.Delete(Path.Combine(Source, "nueva.jpg"));
        Thread.Sleep(1500);
        Assert.True(File.Exists(Path.Combine(Target, "nueva.jpg")));
    }

    [Fact]
    public void MirrorAlsoRemovesWhatDisappears()
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Source).FullName, "a.txt"), "a");
        File.WriteAllText(Path.Combine(Source, "b.txt"), "b");
        Start(mirror: true);

        File.Delete(Path.Combine(Source, "a.txt"));
        File.Move(Path.Combine(Source, "b.txt"), Path.Combine(Source, "c.txt"));
        WaitFor(() => !File.Exists(Path.Combine(Target, "a.txt")) && !File.Exists(Path.Combine(Target, "b.txt")) && File.Exists(Path.Combine(Target, "c.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(Target, "c.txt")));
    }

    [Fact]
    public void ABurstOfChangesEndsUpCompletelyCopied()
    {
        Start(mirror: false);
        for (int i = 0; i < 200; i++)
        {
            File.WriteAllText(Path.Combine(Source, $"f{i}.txt"), new string('x', i));
            if (i % 50 == 0)
            {
                Thread.Sleep(350); // pausas a mitad: habrá pasadas mientras siguen llegando cambios
            }
        }

        // Solo los archivos terminados: mientras se copia, en el destino también hay .mcpart y .mcjournal.
        WaitFor(() => Enumerable.Range(0, 200).All(i =>
        {
            var file = new FileInfo(Path.Combine(Target, $"f{i}.txt"));
            return file.Exists && file.Length == i;
        }));
        Assert.All(Enumerable.Range(0, 200), i => Assert.Equal(i, new FileInfo(Path.Combine(Target, $"f{i}.txt")).Length));
    }

    [Fact]
    public void ValidatesPairsThatWouldWatchThemselves()
    {
        Directory.CreateDirectory(Source);
        Assert.Null(LiveSync.Validate(Source, _temp.Combine("copia")));
        Assert.NotNull(LiveSync.Validate(Source, Path.Combine(Source, "dentro")));
        Assert.NotNull(LiveSync.Validate(Source, _temp.Path)); // la copia sería el propio origen
        Assert.NotNull(LiveSync.Validate(_temp.Combine("no existe"), _temp.Combine("copia")));
        Directory.CreateDirectory(_temp.Combine("copia", "Fotos", "Fotos"));
        Assert.NotNull(LiveSync.Validate(_temp.Combine("copia", "Fotos", "Fotos"), _temp.Combine("copia")));
    }

    [Fact]
    public void AMissingDestinationDriveIsReportedNotHung()
    {
        Directory.CreateDirectory(Source);
        File.WriteAllText(Path.Combine(Source, "a.txt"), "a");
        _sync = new LiveSync(Source, @"Q:\no\existe", mirror: false, () => new CopyOptions { WaitForDevice = true, ProgressInterval = TimeSpan.Zero }, TimeSpan.FromMilliseconds(100));
        _sync.Synced += (_, result) => _passes.Add(result);
        _sync.Start();
        Assert.True(_passes.TryTake(out var result, TimeSpan.FromSeconds(20)));
        Assert.True(result.Failed > 0 || result.Error is not null);
    }
}
