using Stakeout.Core;

namespace Stakeout.Tests.Windowing;

public class UniformColumnsTests
{
    [Fact]
    public void FillsTheWidth_NoEmptyStrip()
    {
        // The field test: 316 px cards (300 + 16 gap) in ~824 px left a ~190 px strip.
        var (columns, width) = UniformColumns.Compute(824, 316, itemCount: 4);

        Assert.Equal(2, columns);
        Assert.Equal(412, width);
    }

    [Theory]
    [InlineData(1644, 316, 4, 4)]  // maximized 1080p, 4 items: 5 would fit, capped at the item count
    [InlineData(1644, 316, 9, 5)]
    [InlineData(316, 316, 3, 1)]
    [InlineData(200, 316, 3, 1)]   // narrower than one item: still one column
    public void ColumnCount(double available, double min, int items, int expected)
    {
        Assert.Equal(expected, UniformColumns.Compute(available, min, items).Columns);
    }

    [Fact]
    public void UnboundedWidth_KeepsMinimumWidth()
    {
        Assert.Equal((3, 316d), UniformColumns.Compute(double.PositiveInfinity, 316, 3));
    }

    [Fact]
    public void ItemWidthNeverBelowMinimum_WhenMoreThanOneColumn()
    {
        for (var w = 316d; w < 3000; w += 7)
        {
            var (columns, width) = UniformColumns.Compute(w, 316, 20);
            if (columns > 1) Assert.True(width >= 316, $"{w}: {width}");
        }
    }

    [Fact]
    public void NonPositiveMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UniformColumns.Compute(800, 0, 3));
    }
}
