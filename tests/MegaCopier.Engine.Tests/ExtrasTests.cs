namespace MegaCopier.Engine.Tests;

/// <summary>
/// Opciones de prioridad baja: filtros por atributo, último acceso y profundidad, .mcignore, máscara de nombres, aplanar,
/// ruta común, bit «Archivo», cambio de hora, compresión NTFS, orden previa y opciones de carpetas.
/// </summary>
public sealed class ExtrasTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, string text = "x")
    {
        string path = _temp.Combine(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private List<string> Copied(string folder = "dst") =>
        [.. Directory.EnumerateFiles(_temp.Combine(folder), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_temp.Combine(folder), f).Replace('\\', '/')).Order(StringComparer.Ordinal)];

    private CopyJobResult Run(CopyOptions options, params string[] sources) =>
        new CopyEngine(options).Run(new CopyJobRequest([.. sources.Select(s => _temp.Combine(s.Split('/')))], _temp.Combine("dst")));

    [Theory]
    [InlineData("foto.jpg", "*.bak", "foto.bak")]
    [InlineData("foto.jpg", "copia de *.*", "copia de foto.jpg")]
    [InlineData("foto.jpg", "*_2024.*", "foto_2024.jpg")]
    [InlineData("léeme", "*.txt", "léeme.txt")]
    [InlineData("foto.jpg", "*.*", "foto.jpg")]
    [InlineData("foto.jpg", "", "foto.jpg")]
    [InlineData("foto.jpg", "nuevo", "nuevo")]
    public void RenameMasksWorkLikeTotalCommander(string name, string mask, string expected) =>
        Assert.Equal(expected, CopyExtras.ApplyMask(name, mask));

    [Fact]
    public void FiltersByAttributeDepthAndLastAccess()
    {
        Write("src/a.txt");
        Write("src/b.txt");
        Write("src/sub/c.txt");
        Write("src/sub/más/d.txt");
        File.SetAttributes(_temp.Combine("src", "a.txt"), FileAttributes.Normal); // sin marca «Archivo»
        File.SetAttributes(_temp.Combine("src", "b.txt"), FileAttributes.Archive | FileAttributes.Temporary);

        var onlyArchive = CopyPlanner.Build([_temp.Combine("src")], _temp.Combine("x"), new FileFilter { RequireAttributes = FileAttributes.Archive, MaxDepth = 0 });
        Assert.Equal(["b.txt"], onlyArchive.Files.Select(f => Path.GetFileName(f.Source)));

        var noTemporary = CopyPlanner.Build([_temp.Combine("src")], _temp.Combine("x"), new FileFilter { SkipAttributes = FileAttributes.Temporary, MaxDepth = 1 });
        Assert.Equal(["a.txt", "c.txt"], noTemporary.Files.Select(f => Path.GetFileName(f.Source)).Order());

        File.SetLastAccessTimeUtc(_temp.Combine("src", "a.txt"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var stale = CopyPlanner.Build([_temp.Combine("src")], _temp.Combine("x"), new FileFilter { AccessedBeforeUtc = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        Assert.Equal(["a.txt"], stale.Files.Select(f => Path.GetFileName(f.Source)));
    }

    [Fact]
    public void AnIgnoreFileExcludesWhatItSays()
    {
        Write("src/.mcignore", "# no copiar\n*.tmp\nnode_modules/\n/build\nfotos/*.raw\n");
        Write("src/informe.docx");
        Write("src/borrador.tmp");
        Write("src/node_modules/x.js");
        Write("src/build/salida.exe");
        Write("src/app/build/salida.exe"); // «/build» solo junto al .mcignore
        Write("src/fotos/a.raw");
        Write("src/fotos/a.jpg");
        Write("src/app/sub/otro.tmp"); // sin barra: en cualquier nivel

        Assert.True(Run(new CopyOptions(), "src").Success);
        Assert.Equal(["src/.mcignore", "src/app/build/salida.exe", "src/fotos/a.jpg", "src/informe.docx"], Copied());
    }

    [Fact]
    public void RenameMaskFlattenAndCommonPath()
    {
        Write("src/2024/a.jpg", "a");
        Write("src/2025/a.jpg", "b");
        Write("src/2025/sub/c.jpg", "c");

        Assert.True(Run(new CopyOptions { Extras = new CopyExtras { Flatten = true, RenameMask = "*.bak" } }, "src").Success);
        Assert.Equal(["a (2).bak", "a.bak", "c.bak"], Copied());
        Assert.Equal(["a", "b"], new[] { File.ReadAllText(_temp.Combine("dst", "a.bak")), File.ReadAllText(_temp.Combine("dst", "a (2).bak")) }.Order());

        Directory.Delete(_temp.Combine("dst"), recursive: true);
        Assert.True(Run(new CopyOptions { Extras = new CopyExtras { KeepRelativePaths = true } }, "src/2024/a.jpg", "src/2025/sub/c.jpg").Success);
        Assert.Equal(["2024/a.jpg", "2025/sub/c.jpg"], Copied());
    }

    [Fact]
    public void ResetsTheArchiveBitAndKeepsNtfsCompression()
    {
        string a = Write("src/a.txt", new string('a', 100_000));
        File.SetAttributes(a, FileAttributes.Archive);
        bool compressed;
        try
        {
            Native.Win32.Compress(a);
            compressed = File.GetAttributes(a).HasFlag(FileAttributes.Compressed);
        }
        catch (IOException)
        {
            compressed = false; // un disco sin compresión NTFS (ReFS, FAT): no se prueba esa parte
        }

        Assert.True(Run(new CopyOptions { Extras = new CopyExtras { ResetArchive = true, KeepNtfsCompression = true } }, "src").Success);
        Assert.False(File.GetAttributes(a).HasFlag(FileAttributes.Archive));
        Assert.Equal(compressed, File.GetAttributes(_temp.Combine("dst", "src", "a.txt")).HasFlag(FileAttributes.Compressed));
    }

    [Fact]
    public void AnHourExactlyIsTheSameFileWhenCompensatingDaylightSaving()
    {
        string source = Write("src/a.txt", "igual");
        string target = Write("dst/src/a.txt", "igual");
        var time = new DateTime(2025, 3, 30, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, time);
        File.SetLastWriteTimeUtc(target, time.AddHours(1));

        var plain = Run(new CopyOptions { Conflict = ConflictPolicy.OverwriteIfNewer, Mode = CopyMode.Mirror }, "src");
        var compensated = Run(new CopyOptions { Mode = CopyMode.Mirror, Extras = new CopyExtras { CompensateDst = true } }, "src");
        Assert.True(compensated.Success);
        Assert.Equal(1, compensated.FilesSkipped);
        Assert.True(plain.Success);
    }

    [Fact]
    public void TheBeforeCommandMustSucceed()
    {
        Write("src/a.txt");
        string marker = _temp.Combine("antes.txt");
        Assert.True(Run(new CopyOptions { Extras = new CopyExtras { BeforeCommand = $"echo hecho> \"{marker}\"" } }, "src").Success);
        Assert.True(File.Exists(marker));

        var error = Assert.Throws<CopyPlanException>(() => Run(new CopyOptions { ForceRecopy = true, Extras = new CopyExtras { BeforeCommand = "exit /b 3" } }, "src"));
        Assert.Contains("3", error.Message);
    }

    [Fact]
    public void FoldersCanKeepTheirOwnTimestampsOrNot()
    {
        Write("src/sub/a.txt");
        var old = new DateTime(2015, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(_temp.Combine("src", "sub"), old);

        Assert.True(Run(new CopyOptions { Extras = new CopyExtras { FolderTimestamps = false } }, "src").Success);
        Assert.NotEqual(old, Directory.GetLastWriteTimeUtc(_temp.Combine("dst", "src", "sub")));

        Directory.Delete(_temp.Combine("dst"), recursive: true);
        Assert.True(Run(new CopyOptions(), "src").Success);
        Assert.Equal(old, Directory.GetLastWriteTimeUtc(_temp.Combine("dst", "src", "sub")));
    }
}
