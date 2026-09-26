using System.Windows;
using System.Windows.Controls;
using Stakeout.Core;

namespace Stakeout.Controls;

/// <summary>
/// Lays equal cards out in rows that always fill the available width
/// (<see cref="UniformColumns"/>): as many columns as fit at
/// <see cref="MinItemWidth"/>, each widened equally. Row height is the tallest
/// card in the row. Children keep their own margins (the gap between cards).
/// </summary>
public sealed class UniformWrapPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(UniformWrapPanel),
        new FrameworkPropertyMetadata(300d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        static v => v is double d && d > 0 && !double.IsInfinity(d));

    /// <summary>Narrowest a card may get, margin included.</summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, itemWidth) = UniformColumns.Compute(availableSize.Width, MinItemWidth, InternalChildren.Count);
        double height = 0, rowHeight = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if ((i + 1) % columns == 0 || i == InternalChildren.Count - 1)
            {
                height += rowHeight;
                rowHeight = 0;
            }
        }
        var width = double.IsInfinity(availableSize.Width) ? columns * itemWidth : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = UniformColumns.Compute(finalSize.Width, MinItemWidth, InternalChildren.Count);
        double y = 0;
        for (var rowStart = 0; rowStart < InternalChildren.Count; rowStart += columns)
        {
            var rowEnd = Math.Min(rowStart + columns, InternalChildren.Count);
            double rowHeight = 0;
            for (var i = rowStart; i < rowEnd; i++) rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
            for (var i = rowStart; i < rowEnd; i++)
                InternalChildren[i].Arrange(new Rect((i - rowStart) * itemWidth, y, itemWidth, rowHeight));
            y += rowHeight;
        }
        return finalSize;
    }
}
