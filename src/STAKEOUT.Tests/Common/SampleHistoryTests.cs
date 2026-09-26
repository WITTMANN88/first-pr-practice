using Stakeout.Core;

namespace Stakeout.Tests.Common;

public class SampleHistoryTests
{
    [Fact]
    public void Empty_HasNoStatistics()
    {
        var h = new SampleHistory(4);
        Assert.Equal(0, h.Count);
        Assert.Null(h.Latest);
        Assert.Null(h.Min);
        Assert.Null(h.Max);
        Assert.Empty(h.ToPoints(100, 50, 0, 100));
    }

    [Fact]
    public void Add_KeepsInsertionOrder_UntilFull()
    {
        var h = new SampleHistory(4);
        foreach (var v in new[] { 40.0, 55, 47 }) h.Add(v);
        Assert.Equal(new[] { 40.0, 55, 47 }, h.ToArray());
        Assert.Equal(47, h.Latest);
        Assert.Equal(40, h.Min);
        Assert.Equal(55, h.Max);
    }

    [Fact]
    public void Add_WhenFull_DropsOldest_AcrossManyWraps()
    {
        var h = new SampleHistory(3);
        for (var i = 1; i <= 10; i++) h.Add(i);
        Assert.Equal(new[] { 8.0, 9, 10 }, h.ToArray());
        Assert.Equal(3, h.Count);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Add_IgnoresNonFiniteSamples(double bad)
    {
        var h = new SampleHistory(3);
        h.Add(50);
        h.Add(bad);
        Assert.Equal(new[] { 50.0 }, h.ToArray());
    }

    [Fact]
    public void Clear_Empties()
    {
        var h = new SampleHistory(3);
        h.Add(1); h.Add(2);
        h.Clear();
        Assert.Equal(0, h.Count);
        h.Add(7);
        Assert.Equal(new[] { 7.0 }, h.ToArray());
    }

    [Fact]
    public void Domain_KeepsFixedBase_AndWidensToIncludeOutliers()
    {
        var h = new SampleHistory(5);
        Assert.Equal((30.0, 100.0), h.Domain(30, 100));   // empty → the base
        h.Add(45); h.Add(60);
        Assert.Equal((30.0, 100.0), h.Domain(30, 100));   // inside → unchanged (85 °C line stays visible)
        h.Add(21); h.Add(104);
        Assert.Equal((21.0, 104.0), h.Domain(30, 100));   // outliers → widened, never clipped
    }

    [Fact]
    public void Domain_RejectsInvertedRange()
    {
        Assert.Throws<ArgumentException>(() => new SampleHistory(2).Domain(100, 30));
    }

    [Fact]
    public void ToPoints_AreRightAligned_WithFixedSpacing()
    {
        var h = new SampleHistory(5);   // spacing = 100 / 4 = 25
        h.Add(0); h.Add(50); h.Add(100);

        var pts = h.ToPoints(100, 40, 0, 100);

        Assert.Equal(new[] { 50.0, 75, 100 }, pts.Select(p => p.X));   // newest at the right edge
        Assert.Equal(new[] { 40.0, 20, 0 }, pts.Select(p => p.Y));     // y grows downward
    }

    [Fact]
    public void ToPoints_FullBuffer_SpansTheWholeWidth()
    {
        var h = new SampleHistory(4);
        for (var i = 0; i < 9; i++) h.Add(i);
        var pts = h.ToPoints(90, 10, 0, 10);
        Assert.Equal(0, pts[0].X);
        Assert.Equal(90, pts[^1].X);
    }

    [Theory]
    [InlineData(85, 48, 30, 100, 48 - (55.0 / 70) * 48)]
    [InlineData(30, 48, 30, 100, 48)]     // bottom
    [InlineData(100, 48, 30, 100, 0)]     // top
    [InlineData(-5, 48, 30, 100, 48)]     // clamped below
    [InlineData(150, 48, 30, 100, 0)]     // clamped above
    [InlineData(50, 48, 60, 60, 24)]      // degenerate domain → middle
    public void ScaleY_MapsAndClamps(double value, double height, double min, double max, double expected)
    {
        Assert.Equal(expected, SampleHistory.ScaleY(value, height, min, max), precision: 9);
    }

    [Fact]
    public void Capacity_BelowTwo_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleHistory(1));
    }
}
