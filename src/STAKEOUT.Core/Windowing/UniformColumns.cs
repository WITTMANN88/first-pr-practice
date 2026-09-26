namespace Stakeout.Core;

/// <summary>
/// Column layout for a grid of equal cards that fills its width: as many
/// columns as fit at the minimum item width, then every column widened equally,
/// so there is no empty strip on the right (a WrapPanel of fixed-width cards
/// leaves up to one card's width unused).
/// </summary>
public static class UniformColumns
{
    /// <summary>
    /// Columns and item width for <paramref name="availableWidth"/>. Unbounded
    /// width (inside a horizontal scroller) keeps the minimum width on one row;
    /// a width narrower than one item still gives one column.
    /// </summary>
    public static (int Columns, double ItemWidth) Compute(double availableWidth, double minItemWidth, int itemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minItemWidth);
        var count = Math.Max(1, itemCount);
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth))
            return (count, minItemWidth);

        var columns = (int)Math.Clamp(Math.Floor(availableWidth / minItemWidth), 1, count);
        var itemWidth = columns == 1 ? Math.Max(availableWidth, 0) : availableWidth / columns;
        return (columns, itemWidth);
    }
}
