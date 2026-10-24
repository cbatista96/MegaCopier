using MegaCopier.Shell;

namespace MegaCopier.Engine.Tests;

public sealed class DragOriginProbeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("DirectUIHWND")]
    [InlineData("SysListView32")]
    [InlineData("SysTreeView32")]
    public void RubberBandAndUnresolvedGesturesNeverHoldMouseRelease(string capture)
    {
        Assert.False(DragOriginProbe.CanAwaitOle(capture, DropSpot.Background, true));
        Assert.False(DragOriginProbe.CanAwaitOle(capture, DropSpot.Unknown, true));
        Assert.False(DragOriginProbe.CanAwaitOle(capture, DropSpot.Item, false));
        Assert.True(DragOriginProbe.CanAwaitOle(capture, DropSpot.Item, true));
    }

    [Fact]
    public void AnUnrelatedCaptureCannotBecomeAnExplorerDrag()
    {
        Assert.False(DragOriginProbe.CanAwaitOle("OtherWindow", DropSpot.Item, true));
    }
}
