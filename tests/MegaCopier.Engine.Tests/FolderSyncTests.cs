using MegaCopier.Engine.Sync;

namespace MegaCopier.Engine.Tests;

public sealed class FolderSyncTests : IDisposable
{
    private static readonly DateTime Old = new(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private readonly TempDir _temp = new();

    public FolderSyncTests()
    {
        Directory.CreateDirectory(Left);
        Directory.CreateDirectory(Right);
    }

    public void Dispose() => _temp.Dispose();

    private string Left => _temp.Combine("izq");

    private string Right => _temp.Combine("der");

    private void Write(string root, string relative, string text, DateTime? modified = null)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, modified ?? Old);
    }

    private static string? Read(string root, string relative) =>
        File.Exists(Path.Combine(root, relative)) ? File.ReadAllText(Path.Combine(root, relative)) : null;

    private CopyJobResult Sync(SyncDirection direction = SyncDirection.TwoWay, bool dryRun = false, params string[] excluded)
    {
        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Sync, SyncDirection = direction, DryRun = dryRun, ProgressInterval = TimeSpan.Zero })
        {
            SyncStateFolder = _temp.Combine("estado"),
            SyncUseRecycleBin = false,
        };
        var result = engine.Run(new CopyJobRequest([Left], Right) { Excluded = excluded });
        Assert.Empty(result.Failures);
        return result;
    }

    private string[] Trash(string root) =>
        Directory.Exists(Path.Combine(root, FolderSync.TrashFolder))
            ? [.. Directory.GetFiles(Path.Combine(root, FolderSync.TrashFolder), "*", SearchOption.AllDirectories).Select(File.ReadAllText)]
            : [];

    [Fact]
    public void FirstSyncMergesBothSidesAndNewerWins()
    {
        Write(Left, "solo-izq.txt", "izq");
        Write(Right, @"sub\solo-der.txt", "der");
        Write(Left, "ambos.txt", "viejo", Old);
        Write(Right, "ambos.txt", "nuevo", Old.AddHours(1));
        Directory.CreateDirectory(Path.Combine(Left, "vacía"));

        var result = Sync();

        Assert.Equal(3, result.FilesCopied);
        Assert.Equal("izq", Read(Right, "solo-izq.txt"));
        Assert.Equal("der", Read(Left, @"sub\solo-der.txt"));
        Assert.Equal("nuevo", Read(Left, "ambos.txt"));
        Assert.True(Directory.Exists(Path.Combine(Right, "vacía")));
        Assert.Equal(["viejo"], Trash(Left)); // lo sobrescrito no se pierde
        Assert.Equal(0, Sync().FilesCopied); // ya está todo igual
    }

    [Fact]
    public void ChangesAndDeletionsTravelBothWays()
    {
        Write(Left, "a.txt", "a");
        Write(Left, "b.txt", "b");
        Write(Left, @"carpeta\c.txt", "c");
        Write(Left, "d.txt", "d");
        Sync();

        Write(Left, "a.txt", "a cambiado en la izquierda", Old.AddDays(1));
        Write(Right, "b.txt", "b cambiado en la derecha", Old.AddDays(1));
        File.Delete(Path.Combine(Right, "d.txt"));
        Directory.Delete(Path.Combine(Left, "carpeta"), recursive: true);
        Write(Right, "nuevo-der.txt", "n");

        var result = Sync();

        Assert.Equal("a cambiado en la izquierda", Read(Right, "a.txt"));
        Assert.Equal("b cambiado en la derecha", Read(Left, "b.txt"));
        Assert.Null(Read(Left, "d.txt"));
        Assert.False(Directory.Exists(Path.Combine(Right, "carpeta")));
        Assert.Equal("n", Read(Left, "nuevo-der.txt"));
        Assert.Equal(2, result.FilesDeleted);
        Assert.Contains("d", Trash(Left));
        Assert.Contains("c", Trash(Right));
    }

    [Fact]
    public void ConflictsKeepTheNewestAndSaveTheOther()
    {
        Write(Left, "x.txt", "base");
        Sync();
        Write(Left, "x.txt", "izquierda (antes)", Old.AddDays(1));
        Write(Right, "x.txt", "derecha (después)", Old.AddDays(2));

        Sync();

        Assert.Equal("derecha (después)", Read(Left, "x.txt"));
        Assert.Equal("derecha (después)", Read(Right, "x.txt"));
        Assert.Contains("izquierda (antes)", Trash(Left));
    }

    [Fact]
    public void ADeletionNeverWinsOverAChange()
    {
        Write(Left, "x.txt", "base");
        Write(Left, @"f\y.txt", "base");
        Sync();
        File.Delete(Path.Combine(Right, "x.txt"));
        Write(Left, "x.txt", "cambiado después", Old.AddDays(1));
        Directory.Delete(Path.Combine(Right, "f"), recursive: true);
        Write(Left, @"f\nuevo.txt", "nuevo en una carpeta borrada al otro lado");

        Sync();

        Assert.Equal("cambiado después", Read(Right, "x.txt"));
        Assert.Equal("nuevo en una carpeta borrada al otro lado", Read(Right, @"f\nuevo.txt"));
        Assert.Null(Read(Left, @"f\y.txt")); // lo que no cambió sí sigue el borrado
    }

    [Fact]
    public void MirrorMakesTheOtherSideIdentical()
    {
        Write(Left, "a.txt", "a");
        Write(Right, "a.txt", "otro", Old.AddDays(5));
        Write(Right, "sobra.txt", "sobra");
        Write(Right, @"sobra\dentro.txt", "dentro");

        Sync(SyncDirection.LeftToRight);

        Assert.Equal("a", Read(Right, "a.txt"));
        Assert.Null(Read(Right, "sobra.txt"));
        Assert.False(Directory.Exists(Path.Combine(Right, "sobra")));
        Assert.Equal(["a"], Directory.GetFiles(Left).Select(File.ReadAllText));

        Write(Left, "b.txt", "b");
        Sync(SyncDirection.RightToLeft);
        Assert.Null(Read(Left, "b.txt"));
    }

    [Fact]
    public void DryRunChangesNothingAndExcludedItemsAreLeftAlone()
    {
        Write(Left, "a.txt", "a");
        Write(Left, "b.txt", "b");
        var preview = Sync(dryRun: true);
        Assert.Equal(2, preview.FilesCopied);
        Assert.Empty(Directory.GetFiles(Right));

        Sync(excluded: "b.txt");
        Assert.Equal("a", Read(Right, "a.txt"));
        Assert.Null(Read(Right, "b.txt"));
        Assert.Equal("b", Read(Left, "b.txt"));
    }

    [Fact]
    public void PlanReasonsForTheCompareView()
    {
        var left = new Dictionary<string, SyncEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["igual.txt"] = new(1, Old),
            ["nuevo.txt"] = new(1, Old),
            ["borrado-der.txt"] = new(1, Old),
        };
        var right = new Dictionary<string, SyncEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["igual.txt"] = new(1, Old.AddSeconds(1)),  // dentro de la tolerancia (FAT32)
        };
        var last = new Dictionary<string, (SyncEntry, SyncEntry)>(StringComparer.OrdinalIgnoreCase)
        {
            ["igual.txt"] = (new(1, Old), new(1, Old)),
            ["borrado-der.txt"] = (new(1, Old), new(1, Old)),
        };

        var plan = FolderSync.Plan(left, right, last, SyncDirection.TwoWay, TimeSpan.FromSeconds(2)).ToDictionary(i => i.Relative);

        Assert.Equal((SyncAction.Equal, SyncReason.Same), (plan["igual.txt"].Action, plan["igual.txt"].Reason));
        Assert.Equal((SyncAction.CopyToRight, SyncReason.New), (plan["nuevo.txt"].Action, plan["nuevo.txt"].Reason));
        Assert.Equal((SyncAction.DeleteLeft, SyncReason.Deleted), (plan["borrado-der.txt"].Action, plan["borrado-der.txt"].Reason));
    }

    [Fact]
    public void RefusesFoldersInsideEachOther()
    {
        Directory.CreateDirectory(Path.Combine(Left, "dentro"));
        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Sync }) { SyncStateFolder = _temp.Combine("estado"), SyncUseRecycleBin = false };
        Assert.Throws<CopyPlanException>(() => engine.Run(new CopyJobRequest([Left], Path.Combine(Left, "dentro"))));
    }
}
