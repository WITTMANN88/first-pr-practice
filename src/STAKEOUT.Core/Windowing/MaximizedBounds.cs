namespace Stakeout.Core;

/// <summary>A screen rectangle in device pixels, Win32 RECT semantics (right and bottom exclusive).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Monitor edges (flags), e.g. those holding an auto-hidden taskbar.</summary>
[Flags]
public enum ScreenEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>
/// Where a borderless (WindowStyle=None) window belongs when maximized.
/// Windows maximizes such a window over the whole monitor, taskbar included;
/// it should fill the work area instead (the monitor minus docked app bars).
/// </summary>
public static class MaximizedBounds
{
    /// <summary>
    /// Maximized rectangle relative to the monitor's origin, the convention of
    /// MINMAXINFO.ptMaxPosition / ptMaxSize. An auto-hidden taskbar reserves no
    /// work area, and a window covering its whole edge would keep it from
    /// sliding in, so one pixel is left free on each such edge.
    /// Returns null when the input is unusable; the caller keeps the default.
    /// </summary>
    public static PixelRect? Compute(PixelRect monitor, PixelRect work, ScreenEdges autoHideEdges)
    {
        if (monitor.IsEmpty || work.IsEmpty) return null;

        // The work area never extends past its monitor.
        var left = Math.Max(work.Left, monitor.Left);
        var top = Math.Max(work.Top, monitor.Top);
        var right = Math.Min(work.Right, monitor.Right);
        var bottom = Math.Min(work.Bottom, monitor.Bottom);

        if (autoHideEdges.HasFlag(ScreenEdges.Left) && left == monitor.Left) left++;
        if (autoHideEdges.HasFlag(ScreenEdges.Top) && top == monitor.Top) top++;
        if (autoHideEdges.HasFlag(ScreenEdges.Right) && right == monitor.Right) right--;
        if (autoHideEdges.HasFlag(ScreenEdges.Bottom) && bottom == monitor.Bottom) bottom--;

        var result = new PixelRect(left - monitor.Left, top - monitor.Top, right - monitor.Left, bottom - monitor.Top);
        return result.IsEmpty ? null : result;
    }
}
