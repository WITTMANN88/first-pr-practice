using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Stakeout.Core;

namespace Stakeout.Infrastructure;

/// <summary>
/// Keeps a borderless window's maximized size to the monitor's work area.
/// Without it Windows maximizes a WindowStyle=None window over the whole
/// monitor: the taskbar is covered and the bottom of the window is hidden
/// under it (the field test lost the "Revert all" button that way).
///
/// Handles WM_GETMINMAXINFO and only rewrites the maximized position and
/// size; the message still reaches WPF, which fills in the track sizes from
/// MinWidth / MinHeight. Values are device pixels on both sides (the app is
/// per-monitor DPI aware), so no DPI conversion is involved.
/// </summary>
internal static class MaximizedWindowHook
{
    private const int WmGetMinMaxInfo = 0x0024;

    /// <summary>Install on <paramref name="window"/> once its HWND exists.</summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += static (sender, _) =>
        {
            var handle = new WindowInteropHelper((Window)sender!).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        };
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            try
            {
                Apply(hwnd, lParam);
            }
            catch (Exception ex) when (ex is ArgumentException or ExternalException)
            {
                // Keep the default (full-monitor) maximize rather than fail the message.
                Logger.LogError("Window.MaxBounds", ex);
            }
        }
        return IntPtr.Zero; // not handled: WPF still applies its min/max track sizes
    }

    private static void Apply(IntPtr hwnd, IntPtr lParam)
    {
        var monitorHandle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero) return;

        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (NativeMethods.GetMonitorInfo(monitorHandle, ref info) == 0) return;

        var monitor = info.Monitor.ToPixelRect();
        var bounds = MaximizedBounds.Compute(monitor, info.Work.ToPixelRect(), AutoHideTaskbarEdges(info.Monitor));
        if (bounds is not { } b) return;

        var mmi = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
        mmi.MaxPosition = new NativeMethods.Point { X = b.Left, Y = b.Top };
        mmi.MaxSize = new NativeMethods.Point { X = b.Width, Y = b.Height };
        Marshal.StructureToPtr(mmi, lParam, fDeleteOld: false);
    }

    /// <summary>Edges of this monitor that hold an auto-hidden taskbar (Windows 8+ query).</summary>
    private static ScreenEdges AutoHideTaskbarEdges(NativeMethods.Rect monitor)
    {
        var edges = ScreenEdges.None;
        (uint Edge, ScreenEdges Flag)[] all =
        {
            (NativeMethods.AbeLeft, ScreenEdges.Left),
            (NativeMethods.AbeTop, ScreenEdges.Top),
            (NativeMethods.AbeRight, ScreenEdges.Right),
            (NativeMethods.AbeBottom, ScreenEdges.Bottom),
        };
        foreach (var (edge, flag) in all)
        {
            var data = new NativeMethods.AppBarData
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(),
                Edge = edge,
                Bounds = monitor,
            };
            if (NativeMethods.SHAppBarMessage(NativeMethods.AbmGetAutoHideBarEx, ref data) != IntPtr.Zero)
                edges |= flag;
        }
        return edges;
    }
}
