using MegaCopier.Shell;

namespace MegaCopier.Engine.Tests;

public sealed class PasteMenuTests
{
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
