using MegaCopier.Shell;

namespace MegaCopier.Engine.Tests;

public sealed class PasteMenuTests
{
    [Theory]
    [InlineData("Pegar")]
    [InlineData("&Pegar")]
    [InlineData("Paste")]
    [InlineData("Einfügen")]
    [InlineData("Coller")]
    [InlineData("貼り付け")]
    public void ReconocePegarDelEscritorioSinIdentificadorNiAtajo(string label) =>
        Assert.True(UiaHitTest.IsUnidentifiedPasteLabel(null, null, label));

    [Fact]
    public void ReconoceIdentificadorNumericoDelMenuClasico() =>
        Assert.True(UiaHitTest.IsUnidentifiedPasteLabel("30980", "", "Pegar"));

    [Theory]
    [InlineData("pastelink", null, "Pegar")]
    [InlineData(null, "Ctrl+Shift+V", "Pegar")]
    [InlineData(null, null, "Pegar acceso directo")]
    [InlineData(null, null, "Copiar")]
    [InlineData(null, null, null)]
    public void LaEtiquetaNoSustituyeOtraOrden(string? id, string? shortcut, string? label) =>
        Assert.False(UiaHitTest.IsUnidentifiedPasteLabel(id, shortcut, label));

    [Theory]
    [InlineData("paste", null)]
    [InlineData("Paste", "")]
    [InlineData(null, "Ctrl+V")]
    [InlineData("", "Ctrl + V")]
    public void ReconocePegarSinDependerDelNombreTraducido(string? id, string? accelerator) =>
        Assert.True(UiaHitTest.IsPasteCommand(id, accelerator));

    [Theory]
    [InlineData("pastelink", "Ctrl+Shift+V")]
    [InlineData("copy", "Ctrl+C")]
    [InlineData("cut", "Ctrl+X")]
    [InlineData("paste-special", null)]
    [InlineData(null, null)]
    public void NoConfundeOtrasOrdenesConPegar(string? id, string? accelerator) =>
        Assert.False(UiaHitTest.IsPasteCommand(id, accelerator));
}
