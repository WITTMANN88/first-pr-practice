using Stakeout.Core;

namespace Stakeout.Tests.Windowing;

public class MaximizedBoundsTests
{
    private static readonly PixelRect FullHd = new(0, 0, 1920, 1080);

    [Fact]
    public void BottomTaskbar_WindowStopsAboveIt()
    {
        // The field-test case: 1920×1080, 40 px taskbar at the bottom; the
        // maximized window used to reach y = 1080 and hide "Revert all".
        var r = MaximizedBounds.Compute(FullHd, new PixelRect(0, 0, 1920, 1040), ScreenEdges.None);

        Assert.Equal(new PixelRect(0, 0, 1920, 1040), r);
        Assert.Equal(1040, r!.Value.Height);
    }

    [Fact]
    public void LeftTaskbar_PositionShiftsRight()
    {
        var r = MaximizedBounds.Compute(FullHd, new PixelRect(62, 0, 1920, 1080), ScreenEdges.None);

        Assert.Equal(new PixelRect(62, 0, 1920, 1080), r);
    }

    [Fact]
    public void SecondaryMonitor_ResultIsRelativeToItsOrigin()
    {
        // A monitor left of and above the primary one: negative virtual-screen coordinates.
        var monitor = new PixelRect(-2560, -360, 0, 1080);
        var work = new PixelRect(-2560, -360, 0, 1032);

        var r = MaximizedBounds.Compute(monitor, work, ScreenEdges.None);

        Assert.Equal(new PixelRect(0, 0, 2560, 1392), r);
    }

    [Theory]
    [InlineData(ScreenEdges.Bottom, 0, 0, 1920, 1079)]
    [InlineData(ScreenEdges.Top, 0, 1, 1920, 1080)]
    [InlineData(ScreenEdges.Left, 1, 0, 1920, 1080)]
    [InlineData(ScreenEdges.Right, 0, 0, 1919, 1080)]
    public void AutoHiddenTaskbar_LeavesOnePixelOnItsEdge(ScreenEdges edge, int l, int t, int r, int b)
    {
        // Auto-hide: the work area is the whole monitor.
        Assert.Equal(new PixelRect(l, t, r, b), MaximizedBounds.Compute(FullHd, FullHd, edge));
    }

    [Fact]
    public void AutoHideEdgeAlreadyInset_NotShrunkTwice()
    {
        // A docked bar on the same edge already moved the work area in.
        var r = MaximizedBounds.Compute(FullHd, new PixelRect(0, 0, 1920, 1040), ScreenEdges.Bottom);

        Assert.Equal(new PixelRect(0, 0, 1920, 1040), r);
    }

    [Fact]
    public void WorkAreaPastMonitor_IsClamped()
    {
        var r = MaximizedBounds.Compute(FullHd, new PixelRect(-8, -8, 1928, 1048), ScreenEdges.None);

        Assert.Equal(new PixelRect(0, 0, 1920, 1048), r);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(100, 100, 50, 200)]
    public void UnusableWorkArea_ReturnsNull(int l, int t, int r, int b)
    {
        Assert.Null(MaximizedBounds.Compute(FullHd, new PixelRect(l, t, r, b), ScreenEdges.None));
    }

    [Fact]
    public void EmptyMonitor_ReturnsNull()
    {
        Assert.Null(MaximizedBounds.Compute(default, FullHd, ScreenEdges.None));
    }
}
