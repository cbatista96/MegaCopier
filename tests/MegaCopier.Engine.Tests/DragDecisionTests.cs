using System.Runtime.InteropServices;
using MegaCopier.Shell;

namespace MegaCopier.Engine.Tests;

public sealed partial class DragDecisionTests
{
    private static readonly string[] Photos = [@"C:\Users\Ana\Fotos\a.jpg", @"C:\Users\Ana\Fotos\b.jpg"];

    private static readonly ViewItem[] Items =
    [
        new("Viaje", @"D:\Copias\Viaje", true),
        new("notas.txt", @"D:\Copias\notas.txt", false),
        new("fotos", @"D:\Copias\fotos.zip", false),
    ];

    private static string? Decide(
        string target = @"D:\Copias", DropSpot spot = DropSpot.Background, string? item = null,
        bool ctrl = false, bool shift = false, bool alt = false, IReadOnlyList<string>? sources = null, string? sourceFolder = @"C:\Users\Ana\Fotos") =>
        DragDecision.Decide(sources ?? Photos, sourceFolder, target, spot, item, Items, ctrl, shift, alt, out _);

    private static DragRequest Request(
        string target = @"D:\Copias", DropSpot spot = DropSpot.Background, string? item = null, bool ctrl = false, bool shift = false,
        IReadOnlyList<string>? sources = null)
    {
        Assert.Null(DragDecision.Decide(sources ?? Photos, @"C:\Users\Ana\Fotos", target, spot, item, Items, ctrl, shift, false, out var request));
        return request!;
    }

    [Fact]
    public void BetweenDrivesCopiesAndWithinADriveMoves()
    {
        Assert.False(Request().Move);
        Assert.True(Request(target: @"C:\Copias").Move);
    }

    [Fact]
    public void CtrlForcesCopyAndShiftForcesMove()
    {
        Assert.False(Request(target: @"C:\Copias", ctrl: true).Move);
        Assert.True(Request(shift: true).Move);
    }

    [Fact]
    public void ShortcutGesturesAreLeftToExplorer()
    {
        Assert.NotNull(Decide(alt: true));
        Assert.NotNull(Decide(ctrl: true, shift: true));
    }

    [Fact]
    public void DroppingOnAFolderItemCopiesIntoIt()
    {
        var request = Request(spot: DropSpot.Item, item: "viaje");
        Assert.Equal(@"D:\Copias\Viaje", request.Destination);
        Assert.Equal(Photos, request.Sources);
    }

    [Fact]
    public void DroppingOnAFileZipOrUnknownSpotIsLeftToExplorer()
    {
        Assert.NotNull(Decide(spot: DropSpot.Item, item: "notas.txt"));
        Assert.NotNull(Decide(spot: DropSpot.Item, item: "fotos"));
        Assert.NotNull(Decide(spot: DropSpot.Item, item: "no existe"));
        Assert.NotNull(Decide(spot: DropSpot.Unknown));
    }

    [Fact]
    public void VirtualPlacesAndSameFolderAreLeftToExplorer()
    {
        Assert.NotNull(DragDecision.Decide(null, @"C:\x", @"D:\y", DropSpot.Background, null, [], false, false, false, out _));
        Assert.NotNull(DragDecision.Decide(Photos, @"C:\Users\Ana\Fotos", null, DropSpot.Background, null, [], false, false, false, out _));
        Assert.NotNull(Decide(target: @"C:\Users\Ana\Fotos\"));
    }

    [Fact]
    public void CtrlInTheSameFolderCreatesAnInPlaceDuplicate()
    {
        var request = Request(target: @"C:\Users\Ana\Fotos\", ctrl: true);
        Assert.True(request.DuplicateInPlace);
        Assert.False(request.Move);
        Assert.False(Request(ctrl: true).DuplicateInPlace);
        Assert.NotNull(Decide(target: @"C:\Users\Ana\Fotos", shift: true));
        Assert.Null(DragDecision.Decide(Photos, null, @"C:\Users\Ana\Fotos", DropSpot.Background, null, [], true, false, false, out request));
        Assert.True(request!.DuplicateInPlace);
    }

    [Fact]
    public void FromSearchResultsOrHomeTheFolderOfEachItemCounts()
    {
        // En una búsqueda o en Inicio el origen no es una carpeta: vale, salvo si se suelta donde ya están.
        Assert.Null(Decide(sourceFolder: null));
        Assert.NotNull(Decide(sourceFolder: null, target: @"C:\Users\Ana\Fotos"));
    }

    [Fact]
    public void DrivesInThisPcAndNavigationPaneFoldersAreTargets()
    {
        // «Este equipo» es una vista virtual, pero sus unidades tienen ruta.
        ViewItem[] drives = [new("Disco local (C:)", @"C:\", true), new("Nuevo vol (F:)", @"F:\", true), new("Red", null, true)];
        Assert.Null(DragDecision.Decide(Photos, @"C:\Users\Ana\Fotos", null, DropSpot.Item, "Nuevo vol (F:)", drives, false, false, false, out var request));
        Assert.Equal(@"F:\", request!.Destination);
        Assert.False(request.Move);
        Assert.NotNull(DragDecision.Decide(Photos, @"C:\Users\Ana\Fotos", null, DropSpot.Item, "Red", drives, false, false, false, out _));
        Assert.NotNull(DragDecision.Decide(Photos, @"C:\Users\Ana\Fotos", null, DropSpot.Background, null, drives, false, false, false, out _));

        // Panel de navegación: la carpeta (o unidad) bajo el puntero llega ya resuelta.
        Assert.Null(DragDecision.Decide(Photos, @"C:\Users\Ana\Fotos", @"C:\", DropSpot.Folder, null, [], false, false, false, out request));
        Assert.True(request!.Move);
        Assert.NotNull(DragDecision.Decide(Photos, @"C:\Users\Ana\Fotos", null, DropSpot.Folder, null, [], false, false, false, out _));
    }
    [Fact]
    public void AFolderIntoItselfAndProgramsAreLeftToExplorer()
    {
        Assert.NotNull(Decide(target: @"C:\Users\Ana\Fotos\a.jpg\dentro", sources: [@"C:\Users\Ana\Fotos\a.jpg"]));
        Assert.NotNull(Decide(sources: [@"C:\Users\Ana\Fotos\setup.exe"]));
        Assert.True(Request(sources: [@"C:\Users\Ana\Fotos\setup.exe"], shift: true).Move);
    }

    [Fact]
    public void UncSharesCountAsTheirOwnVolume()
    {
        Assert.True(DragDecision.SameVolume(@"\\nas\fotos\a.jpg", @"\\nas\fotos\b"));
        Assert.False(DragDecision.SameVolume(@"\\nas\fotos\a.jpg", @"\\nas\copias\b"));
        Assert.False(DragDecision.SameVolume(@"C:\a", @"D:\a"));
    }

    // Contra el Escritorio real, solo lectura: valida las llamadas COM (IShellWindows, IFolderView, IShellItemArray).
    [Fact]
    public void ReadsTheDesktopFolderAndItems()
    {
        nint view = DesktopListView();
        if (view == 0)
        {
            return; // sin Escritorio (sesión de servicio)
        }

        ViewSnapshot? snapshot = null;
        RunSta(() => snapshot = ExplorerLocator.Snapshot(view, selection: true, items: true));
        Assert.NotNull(snapshot);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), snapshot!.Folder);
        Assert.NotNull(snapshot.Selection);
        Assert.All(snapshot.Items, i => Assert.False(string.IsNullOrEmpty(i.DisplayName)));
        Assert.All(snapshot.Items.Where(i => i.IsFolder), i => Assert.True(Directory.Exists(i.Path)));
        Assert.All(snapshot.Items.Where(i => i.Path is not null && !i.IsFolder), i => Assert.True(File.Exists(i.Path) || Directory.Exists(i.Path)));
    }


    // Contra el shell real, solo lectura: la unidad del sistema se encuentra por su nombre visible («Disco local (C:)»).
    [Fact]
    public void ResolvesNavigationPaneNamesToFolders()
    {
        string system = Path.GetPathRoot(Environment.SystemDirectory)!;
        string? resolved = null, wrapped = null, missing = "x", missingChild = "x", missingAncestor = "x";
        RunSta(() =>
        {
            string name = VisibleName(system);
            resolved = ShellNamespace.Resolve([name]);
            wrapped = ShellNamespace.Resolve(["Escritorio (OneDrive)", name]);
            missing = ShellNamespace.Resolve(["Ninguna carpeta se llama así 7f3c"]);
            missingChild = ShellNamespace.Resolve([name, "Ninguna carpeta se llama así 7f3c", name]);
            missingAncestor = ShellNamespace.Resolve(["Escritorio (OneDrive)", "Ninguna carpeta se llama así 7f3c", name]);
        });
        Assert.Equal(system, resolved, ignoreCase: true);
        Assert.Equal(system, wrapped, ignoreCase: true);
        Assert.Null(missing);
        Assert.Null(missingChild);
        Assert.Null(missingAncestor);
    }

    private static unsafe string VisibleName(string path)
    {
        var info = new byte[1024];
        fixed (byte* buffer = info)
        {
            SHGetFileInfoW(path, 0, buffer, 696, 0x200);
            return new string((char*)(buffer + 16));
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial nint SHGetFileInfoW(string path, uint attributes, byte* info, uint size, uint flags);
    [Fact]
    public void HitTestingAnyPointDoesNotThrow()
    {
        RunSta(() => UiaHitTest.At(1, 1, out _));
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }
    }

    private static nint DesktopListView()
    {
        nint view = FindWindowExW(FindWindowW("Progman", null), 0, "SHELLDLL_DefView", null);
        for (nint worker = FindWindowExW(0, 0, "WorkerW", null); view == 0 && worker != 0; worker = FindWindowExW(0, worker, "WorkerW", null))
        {
            view = FindWindowExW(worker, 0, "SHELLDLL_DefView", null);
        }

        return view == 0 ? 0 : FindWindowExW(view, 0, "SysListView32", null);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string? className, string? title);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint after, string? className, string? title);
}

public sealed class OtherFileManagersTests
{
    [Theory]
    [InlineData(@"c:\Fotos\*.*", @"c:\Fotos")]
    [InlineData(@"d:\*.*", @"d:\")]
    [InlineData(@"\nas\fotos\2025\*.jpg", @"\nas\fotos\2025")]
    [InlineData(@"c:\Sin filtro", @"c:\Sin filtro")]
    [InlineData("", null)]
    public void TotalCommanderPanelPaths(string text, string? expected) =>
        Assert.Equal(expected, MegaCopier.Shell.OtherFileManagers.TotalCommanderPath(text));

    [Fact]
    public void DirectoryOpusPaths()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <results command="paths" result="1">
              <path active_lister="1" active_tab="1" lister="0x1" side="1" tab="0x2">C:\Users\Ana\Fotos</path>
              <path lister="0x1" side="2" tab="0x3">D:\Copias</path>
            </results>
            """;
        Assert.Equal([@"C:\Users\Ana\Fotos", @"D:\Copias"], MegaCopier.Shell.OtherFileManagers.DirectoryOpusPaths(xml));
    }
}
