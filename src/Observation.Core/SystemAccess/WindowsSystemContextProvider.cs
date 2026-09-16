using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Observation.Core.SystemAccess;

/// <summary>
/// Реальное определение версии/редакции Windows и модели CPU — см. «Определение версии
/// и редакции Windows» в плане: точная сборка через RtlGetVersion (не Environment.OSVersion,
/// который может соврать из-за манифеста совместимости), EditionID/DisplayVersion из реестра,
/// модель CPU через WMI (System.Management — явно одобрено в «Исполнение и системный доступ»).
/// </summary>
public sealed class WindowsSystemContextProvider : ISystemContextProvider
{
    private const string CurrentVersionPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private readonly IRegistryAccessor _registry;

    public WindowsSystemContextProvider(IRegistryAccessor registry) => _registry = registry;

    public SystemContext GetCurrent()
    {
        var buildNumber = GetBuildNumberViaRtlGetVersion() ?? GetBuildNumberViaRegistryFallback();
        var editionId = ReadStringValue("EditionID") ?? "Unknown";
        var displayVersion = ReadStringValue("DisplayVersion") ?? "Unknown";
        var cpuModel = TryGetCpuModel();

        return new SystemContext(buildNumber, editionId, displayVersion, cpuModel);
    }

    private int? GetBuildNumberViaRtlGetVersion()
    {
        try
        {
            var info = new RTL_OSVERSIONINFOEX { dwOSVersionInfoSize = (uint)Marshal.SizeOf<RTL_OSVERSIONINFOEX>() };
            return RtlGetVersion(ref info) == 0 ? (int)info.dwBuildNumber : null;
        }
        catch (Exception)
        {
            // ntdll.dll недоступен (не Windows, либо блокировка политикой) — используем реестр как запасной путь.
            return null;
        }
    }

    private int GetBuildNumberViaRegistryFallback() =>
        int.TryParse(ReadStringValue("CurrentBuildNumber"), out var build) ? build : 0;

    private string? ReadStringValue(string valueName) =>
        _registry.TryReadValue(RegistryHive.LocalMachine, CurrentVersionPath, valueName, out var data, out _)
            ? data as string
            : null;

    private static string? TryGetCpuModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                return item["Name"] as string;
            }
        }
        catch (Exception)
        {
            // WMI недоступен/заблокирован — применимость по CPU просто не будет сужена, это не критично.
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RTL_OSVERSIONINFOEX
    {
        public uint dwOSVersionInfoSize;
        public uint dwMajorVersion;
        public uint dwMinorVersion;
        public uint dwBuildNumber;
        public uint dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX versionInfo);
}
