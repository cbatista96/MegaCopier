using MegaCopier.Shell;

namespace MegaCopier.Engine.Tests;

public sealed class PasteDecisionTests
{
    [Theory]
    [InlineData(0x0100u, true)]
    [InlineData(0x0104u, true)]
    [InlineData(0x0101u, false)]
    [InlineData(0x0105u, false)]
    public void F10TambienLlegaComoTeclaDeSistema(uint message, bool expected) =>
        Assert.Equal(expected, PasteInterceptor.IsKeyDownMessage(message));
    [Theory]
    [InlineData(ClipboardOperation.Copy, @"F:\origen\foto.jpg", @"C:\Users\Prueba\Desktop", false)]
    [InlineData(ClipboardOperation.Move, @"C:\origen\carpeta", @"C:\Users\Prueba\Desktop", false)]
    [InlineData(ClipboardOperation.Copy, @"C:\Users\Prueba\Desktop\foto.jpg", @"C:\Users\Prueba\Desktop", true)]
    [InlineData(ClipboardOperation.Copy, @"C:\Users\Prueba\Desktop\carpeta\", @"c:\users\prueba\desktop\", true)]
    [InlineData(ClipboardOperation.Copy, @"\\nas\datos\foto.jpg", @"\\nas\copia", false)]
    [InlineData(ClipboardOperation.Move, @"C:\fuente\archivo.txt", @"F:\", false)]
    public void ConservaLaOperacionYDuplicaSoloEnElMismoSitio(ClipboardOperation operation, string source, string target, bool duplicate)
    {
        Assert.Null(PasteDecision.Decide(new([source], operation), target, out var request));
        Assert.NotNull(request);
        Assert.Equal(operation, request.Operation);
        Assert.Equal(duplicate, request.DuplicateInPlace);
    }

    [Fact]
    public void CortarYPegarEnSuPropiaCarpetaEsUnaOperacionAtendidaSinTransferencia()
    {
        Assert.Null(PasteDecision.Decide(new([@"C:\destino\a.txt", @"C:\destino\b"], ClipboardOperation.Move), @"C:\destino", out var request));
        Assert.Null(request);
    }

    [Theory]
    [InlineData(ClipboardOperation.Copy)]
    [InlineData(ClipboardOperation.Move)]
    public void TrataSeleccionesConOrigenesMezclados(ClipboardOperation operation)
    {
        string[] original = [@"C:\destino\ya.txt", @"F:\otra\nuevo.txt"];
        Assert.Null(PasteDecision.Decide(new(original, operation), @"C:\destino", out var request));
        Assert.NotNull(request);
        Assert.Equal(operation == ClipboardOperation.Copy ? 2 : 1, request.Sources.Count);
        Assert.Contains(@"F:\otra\nuevo.txt", request.Sources);
        Assert.Equal(original, request.ClipboardSources);
        Assert.Equal(operation == ClipboardOperation.Copy, request.DuplicateInPlace);
    }

    [Fact]
    public void UnaRedDesconectadaOUnArchivoBorradoNoEnviaUnPegadoReconocidoAOtroCopiador()
    {
        Assert.Null(PasteDecision.Decide(new([@"\\servidor-inexistente\datos\a.txt"], ClipboardOperation.Copy), @"F:\destino-inexistente", out var request));
        Assert.NotNull(request); // El motor presenta el error o espera al destino, conservando la petición.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("relativa")]
    public void NoConvierteUbicacionesVirtualesEnRutasDeDisco(string? destination) =>
        Assert.NotNull(PasteDecision.Decide(new([@"C:\a.txt"], ClipboardOperation.Copy), destination, out _));

    [Fact]
    public void ElFondoNoUsaUnaSeleccionAntiguaComoDestino()
    {
        var snapshot = new ViewSnapshot(@"C:\Desktop", [@"C:\Desktop\Otra"], [new("Otra", @"C:\Desktop\Otra", true)]);
        Assert.Equal(@"C:\Desktop", PasteDecision.ContextDestination(snapshot, DropSpot.Background, null));
        Assert.Equal(@"C:\Desktop\Otra", PasteDecision.ContextDestination(snapshot, DropSpot.Item, "Otra"));
    }

    [Fact]
    public void UnaUnidadEnEsteEquipoEsUnDestinoFisicoAunqueLaVistaSeaVirtual()
    {
        var snapshot = new ViewSnapshot(null, null, [new("Disco (F:)", @"F:\", true)]);
        Assert.Equal(@"F:\", PasteDecision.ContextDestination(snapshot, DropSpot.Item, "Disco (F:)"));
        Assert.Null(PasteDecision.ContextDestination(snapshot, DropSpot.Background, null));
    }

    [Fact]
    public void NoConfundeArchivosNiNombresAmbiguosConCarpetas()
    {
        var snapshot = new ViewSnapshot(@"C:\datos", null, [new("archivo", @"C:\datos\archivo.txt", false), new("igual", @"C:\uno", true), new("igual", @"C:\dos", true)]);
        Assert.Null(PasteDecision.ContextDestination(snapshot, DropSpot.Item, "archivo"));
        Assert.Null(PasteDecision.ContextDestination(snapshot, DropSpot.Item, "igual"));
        Assert.Null(PasteDecision.ContextDestination(snapshot, DropSpot.Unknown, null));
    }

    [Theory]
    [InlineData("Progman", "XamlExplorerHostIslandWindow_WASDK", true)]
    [InlineData("WorkerW", "XamlExplorerHostIslandWindow", true)]
    [InlineData("CabinetWClass", "XamlExplorerHostIslandWindow_WASDK", false)]
    [InlineData("Progman", "Chrome_WidgetWin_1", false)]
    [InlineData("WorkerW", "CabinetWClass", false)]
    public void ReconoceElHostIndependienteDelMenuModernoSoloParaElEscritorio(string source, string host, bool expected) =>
        Assert.Equal(expected, ExplorerPasteMenu.IsDesktopMenuHost(source, host));

    [Theory]
    [InlineData(0x56u, true, false, false, false, true)]
    [InlineData(0x2Du, false, true, false, false, true)]
    [InlineData(0x56u, true, true, false, false, false)]
    [InlineData(0x56u, true, false, true, false, false)]
    [InlineData(0x56u, true, false, false, true, false)]
    [InlineData(0x2Du, true, false, false, false, false)]
    [InlineData(0x43u, true, false, false, false, false)]
    public void ReconoceLosAtajosDePegarSinInterferirConCopiarNiPegarAccesosDirectos(uint key, bool ctrl, bool shift, bool alt, bool windows, bool expected) =>
        Assert.Equal(expected, PasteInterceptor.IsPasteShortcut(key, ctrl, shift, alt, windows));
}
