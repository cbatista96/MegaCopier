using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

/// <summary>Reordenar arrastrando, soltar en una posición, orden guardado y cola reordenable.</summary>
public sealed class OrderTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Names(TransferList list) => string.Join("", list.Snapshot().Select(i => i.Source));

    [Fact]
    public void MoveBeforeAndInsertRespectStartedFiles()
    {
        var list = new TransferList();
        foreach (string name in new[] { "A", "B", "C", "D" })
        {
            list.Add(name, name, 1, default);
        }

        var a = list.TakeNext()!; // A ya se está copiando
        int Id(string name) => list.Snapshot().First(i => i.Source == name).Id;

        Assert.True(list.MoveBefore(Id("D"), Id("B")));
        Assert.Equal("ADBC", Names(list));

        Assert.True(list.MoveBefore(Id("B"), null));
        Assert.Equal("ADCB", Names(list));

        // Nada puede ponerse delante de lo que ya empezó.
        Assert.True(list.MoveBefore(Id("C"), a.Id));
        Assert.Equal("ACDB", Names(list));
        Assert.False(list.MoveBefore(a.Id, null));

        list.Insert("E", "E", 1, default, Id("D"));
        Assert.Equal("ACEDB", Names(list));
        list.Insert("F", "F", 1, default, a.Id);
        Assert.Equal("AFCEDB", Names(list));

        Assert.Equal(["F", "C", "E", "D", "B"], list.PendingOrder());
    }

    [Fact]
    public void SavedOrderIsUsedWhenTheJobRuns()
    {
        string source = _temp.Combine("src");
        Directory.CreateDirectory(source);
        foreach (string name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        {
            File.WriteAllText(Path.Combine(source, name), name);
        }

        var engine = new CopyEngine(new CopyOptions { ProgressInterval = TimeSpan.Zero });
        var started = new List<string>();
        engine.Files.Changed += change =>
        {
            if (change.Kind == TransferListChangeKind.Updated && change.Item.State == FileState.Copying)
            {
                started.Add(Path.GetFileName(change.Item.Source));
            }
        };

        var request = new CopyJobRequest([source], _temp.Combine("dst"))
        {
            Order = [Path.Combine(source, "d.txt"), Path.Combine(source, "b.txt")],
        };
        engine.Run(request);

        Assert.Equal(["d.txt", "b.txt", "a.txt", "c.txt"], started);
    }

    [Fact]
    public void QueueCanBeReordered()
    {
        var queue = new CopyQueue(new JobStore(_temp.Combine("jobs")), r => r.ToOptions());
        var later = DateTime.UtcNow.AddHours(1); // programadas: no empiezan durante la prueba
        string src = _temp.Combine("x.txt");
        File.WriteAllText(src, "x");
        var one = queue.Submit([src], _temp.Combine("d1"), new CopyOptions(), later);
        var two = queue.Submit([src], _temp.Combine("d2"), new CopyOptions(), later);
        var three = queue.Submit([src], _temp.Combine("d3"), new CopyOptions(), later);

        int reordered = 0;
        queue.JobsReordered += () => reordered++;
        queue.MoveJob(three, one);
        Assert.Equal([three, one, two], queue.Jobs);
        queue.MoveJob(three, null);
        Assert.Equal([one, two, three], queue.Jobs);
        Assert.Equal(2, reordered);
    }

    [Fact]
    public void DroppingOnAQueuedJobAddsSources()
    {
        var queue = new CopyQueue(new JobStore(_temp.Combine("jobs")), r => r.ToOptions());
        string a = _temp.Combine("a.txt");
        string b = _temp.Combine("b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        var job = queue.Submit([a], _temp.Combine("dst"), new CopyOptions(), DateTime.UtcNow.AddHours(1));

        var same = queue.AddToJob(job, [b, a]);

        Assert.Same(job, same);
        Assert.Equal([a, b], job.Record.Sources);
    }
}
