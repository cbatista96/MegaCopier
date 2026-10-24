using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

public sealed class DuplicateInPlaceTests : IDisposable
{
    private readonly TempDir _temp = new();
    public void Dispose() => _temp.Dispose();

    [Fact]
    public void CopiesWholeFoldersAndFilesWithoutTouchingExistingItems()
    {
        string source = _temp.Combine("Fotos.viaje");
        Directory.CreateDirectory(Path.Combine(source, "Sub"));
        Directory.CreateDirectory(Path.Combine(source, "Vacia"));
        File.WriteAllText(Path.Combine(source, "Sub", "foto.txt"), "original");
        File.WriteAllText(_temp.Combine("Fotos.viaje (2)"), "ocupado");
        string file = _temp.Combine("nota.txt");
        File.WriteAllText(file, "nota");
        var options = new CopyOptions { DuplicateInPlace = true, FolderConflict = FolderConflictPolicy.Skip, Conflict = ConflictPolicy.Overwrite };
        var engine = new CopyEngine(options);
        IReadOnlyDictionary<string, string>? names = null;
        engine.FolderDecided += (_, renamed) => names = renamed;

        var result = engine.Run(new CopyJobRequest([source, file], _temp.Path));

        Assert.True(result.Success);
        Assert.Equal("original", File.ReadAllText(_temp.Combine("Fotos.viaje (3)", "Sub", "foto.txt")));
        Assert.True(Directory.Exists(_temp.Combine("Fotos.viaje (3)", "Vacia")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(source, "Sub", "foto.txt")));
        Assert.Equal("ocupado", File.ReadAllText(_temp.Combine("Fotos.viaje (2)")));
        Assert.Equal("nota", File.ReadAllText(_temp.Combine("nota (2).txt")));
        Assert.NotNull(names);

        // Reanudar conserva los mismos nombres: no crea otra copia numerada.
        result = new CopyEngine(options).Run(new CopyJobRequest([source, file], _temp.Path) { Resumed = true, RenamedFolders = names });
        Assert.True(result.Success);
        Assert.False(Directory.Exists(_temp.Combine("Fotos.viaje (4)")));
        Assert.False(File.Exists(_temp.Combine("nota (3).txt")));
        Assert.True(CopyJobRecord.New([source], _temp.Path, options).ToOptions().DuplicateInPlace);
    }

    [Fact]
    public void DiscardingADuplicateOnlyCleansItsOwnPartialFile()
    {
        string source = _temp.Combine("nota.txt");
        File.WriteAllText(source, "original");
        string duplicate = _temp.Combine("nota (2).txt");
        File.WriteAllText(duplicate + ".mcpart", "incompleto");
        File.WriteAllText(source + ".mcpart", "ajeno");
        CopyEngine.DeletePartialFiles(new CopyJobRequest([source], _temp.Path)
        { RenamedFolders = new Dictionary<string, string> { [source] = "nota (2).txt" } });
        Assert.False(File.Exists(duplicate + ".mcpart"));
        Assert.True(File.Exists(source + ".mcpart"));
        Assert.Equal("original", File.ReadAllText(source));
    }

    [Fact]
    public void NeverMovesOrCopiesAFolderInsideItself()
    {
        string source = _temp.Combine("Origen");
        Directory.CreateDirectory(source);
        Assert.Throws<ArgumentException>(() => new CopyEngine(new CopyOptions { DuplicateInPlace = true, Move = true })
            .Run(new CopyJobRequest([source], _temp.Path)));
        Assert.Throws<CopyPlanException>(() => new CopyEngine(new CopyOptions { DuplicateInPlace = true })
            .Run(new CopyJobRequest([source], source)));
        Assert.True(Directory.Exists(source));
    }
}
