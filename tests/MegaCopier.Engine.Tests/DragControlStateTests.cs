using MegaCopier.Shell;

namespace MegaCopier.Engine.Tests;

public sealed class DragControlStateTests
{
    [Theory]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    public void KeepsPhysicalControlHeldAcrossTheSyntheticRelease(int key)
    {
        var state = new DragControlState();
        Assert.False(state.Block(key, up: false, ownInput: false));
        state.Suspended = true;
        Assert.False(state.Block(key, up: true, ownInput: true));
        state.Suspended = false;
        Assert.False(state.Block(key, up: false, ownInput: true));
    }

    [Theory]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    public void DoesNotRestoreControlIfTheUserReleasedItDuringCancellation(int key)
    {
        var state = new DragControlState();
        state.Reset(key == 0xA2, key == 0xA3);
        state.Suspended = true;
        Assert.True(state.Block(key, up: true, ownInput: false));
        state.Suspended = false;
        Assert.True(state.Block(key, up: false, ownInput: true));
        Assert.False(state.Block(key, up: false, ownInput: false));
    }

    [Fact]
    public void HandlesBothControlsAndASecondPressWithoutBlockingOtherKeys()
    {
        var state = new DragControlState();
        state.Reset(true, true);
        state.Suspended = true;
        Assert.True(state.Block(0xA2, up: true, ownInput: false));
        Assert.True(state.Block(0xA2, up: false, ownInput: false));
        Assert.True(state.Block(0xA3, up: true, ownInput: false));
        Assert.False(state.Block(0x1B, up: false, ownInput: true));
        Assert.False(state.Block(0x10, up: false, ownInput: false));
        state.Suspended = false;
        Assert.False(state.Block(0xA2, up: false, ownInput: true));
        Assert.True(state.Block(0xA3, up: false, ownInput: true));
    }
}
