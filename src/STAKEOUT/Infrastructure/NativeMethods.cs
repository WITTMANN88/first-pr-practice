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

    // --- starting a process as the desktop user (not elevated) -------------

    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint TokenDuplicate = 0x0002;
    public const uint TokenAssignPrimary = 0x0001;
    public const uint TokenQuery = 0x0008;
    public const uint TokenAdjustDefault = 0x0080;
    public const uint TokenAdjustSessionId = 0x0100;
    public const int SecurityImpersonation = 2;
    public const int TokenPrimary = 1;

    /// <summary>STARTUPINFOW with string fields as pointers (kept blittable).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr OpenProcess(uint access, int inheritHandle, uint processId);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes,
        int impersonationLevel, int tokenType, out IntPtr newToken);

    /// <summary>Strings are passed as HGLOBAL UTF-16 pointers; the command line buffer must be writable.</summary>
    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int CreateProcessWithTokenW(IntPtr token, uint logonFlags, IntPtr applicationName,
        IntPtr commandLine, uint creationFlags, IntPtr environment, IntPtr currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int CloseHandle(IntPtr handle);

    // --- icons -----------------------------------------------------------------

    /// <summary>File name as an HGLOBAL UTF-16 pointer; negative index = resource id.</summary>
    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint ExtractIconExW(IntPtr file, int index, out IntPtr largeIcon, IntPtr smallIcons, uint count);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int DestroyIcon(IntPtr icon);
}
