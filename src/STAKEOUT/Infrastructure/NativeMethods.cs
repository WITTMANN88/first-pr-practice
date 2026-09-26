using System.Runtime.InteropServices;
using Stakeout.Core;

namespace Stakeout.Infrastructure;

/// <summary>
/// Win32 declarations. Only blittable signatures (no runtime marshalling, safe
/// under trimming), and every library is loaded from System32 only.
/// </summary>
internal static class NativeMethods
{
    public const uint MonitorDefaultToNearest = 2;

    public const uint AbmGetAutoHideBarEx = 0x0B;
    public const uint AbeLeft = 0;
    public const uint AbeTop = 1;
    public const uint AbeRight = 2;
    public const uint AbeBottom = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Bounds;
        public IntPtr Param;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    /// <returns>Nonzero on success (a Win32 BOOL, kept as int to stay blittable).</returns>
    [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "GetMonitorInfoW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);
}
