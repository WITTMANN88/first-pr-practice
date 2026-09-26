using System.Management;
using LibreHardwareMonitor.Hardware;
using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Collects system information from three complementary sources:
///   * LibreHardwareMonitorLib — CPU package temperature (WMI thermal zones are
///     unreliable on modern, especially mobile, CPUs).
///   * WMI — motherboard, RAM, disks, GPU names, OS product name.
///   * Registry — accurate Windows build string, motherboard fallback.
/// Everything runs off the UI thread and every source is guarded so a single
/// failing query never blanks the whole page.
/// </summary>
public sealed class SystemInfoService : IDisposable
{
    private Computer? _computer;
    private readonly object _lhmGate = new();

    /// <summary>Gather a full snapshot. Safe to call repeatedly.</summary>
    public Task<SystemInfoModel> GatherAsync() => Task.Run(() =>
    {
        // Initializers run in order: same sequence of queries as before.
        return new SystemInfoModel
        {
            CpuName = GetCpuName(),
            CpuTemperatureC = GetCpuTemperature(),
            Motherboard = GetMotherboard(),
            Gpus = GetGpus(),
            RamSummary = GetRam(),
            DiskSummary = GetDisks(),
            WindowsVersion = GetWindowsVersion(),
        };
    });

    /// <summary>Re-read only the CPU temperature (cheap; used for live polling).</summary>
    public Task<double?> RefreshTemperatureAsync() => Task.Run(GetCpuTemperature);

    // --- CPU ---------------------------------------------------------------

    private static string GetCpuName()
    {
        // Registry is the fastest, most reliable name source.
        var name = RegistryHelper.ReadString(RegHive.LocalMachine,
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();

        return WmiFirst("Win32_Processor", "Name") ?? "—";
    }

    /// <summary>
    /// Read the CPU package temperature via LibreHardwareMonitor. Returns null if
    /// no thermal sensor is exposed (common on some desktops / VMs).
    /// </summary>
    private double? GetCpuTemperature()
    {
        try
        {
            lock (_lhmGate)
            {
                _computer ??= OpenComputer();
                var visitor = new UpdateVisitor();

                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType != HardwareType.Cpu) continue;
                    hw.Accept(visitor);

                    // Prefer an explicit package sensor, else average the cores.
                    double? package = null;
                    var coreTemps = new List<double>();
                    foreach (var sensor in hw.Sensors)
                    {
                        if (sensor.SensorType != SensorType.Temperature || sensor.Value is not float v)
                            continue;
                        if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase))
                        {
                            package = v;
                        }
                        else
                        {
                            coreTemps.Add(v);
                        }
                    }
                    if (package.HasValue) return Math.Round(package.Value, 1);
                    if (coreTemps.Count > 0) return Math.Round(coreTemps.Average(), 1);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("CPU temperature", ex);
        }
        return null;
    }

    private static Computer OpenComputer()
    {
        var c = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
        };
        c.Open();
        return c;
    }

    // --- Motherboard -------------------------------------------------------

    private static string GetMotherboard()
    {
        // WMI first, then registry fallback (HARDWARE\DESCRIPTION path).
        var man = WmiFirst("Win32_BaseBoard", "Manufacturer");
        var prod = WmiFirst("Win32_BaseBoard", "Product");
        var combined = string.Join(" ", new[] { man, prod }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(combined)) return combined.Trim();

        var biosBoard = RegistryHelper.ReadString(RegHive.LocalMachine,
            @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct");
        return string.IsNullOrWhiteSpace(biosBoard) ? "—" : biosBoard.Trim();
    }

    // --- GPUs --------------------------------------------------------------

    private static List<GpuInfo> GetGpus()
    {
        var list = new List<GpuInfo>();
        try
        {
            using var searcher = MakeSearcher(
                "SELECT Name, AdapterCompatibility FROM Win32_VideoController");
            Wmi.ForEach(searcher, mo =>
            {
                var name = mo["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(new GpuInfo { Name = name.Trim(), Kind = ClassifyGpu(name) });
                return true;
            });
        }
        catch (Exception ex)
        {
            LogWmiError("GPU enumeration", ex);
        }
        if (list.Count == 0) list.Add(new GpuInfo { Name = "—" });
        return list;
    }

    /// <summary>Heuristic: Intel/AMD APU graphics are integrated, the rest discrete.</summary>
    private static string ClassifyGpu(string name)
    {
        bool Has(string part) => name.Contains(part, StringComparison.OrdinalIgnoreCase);

        if (Has("intel") && (Has("uhd") || Has("hd graphics") || Has("iris")))
            return Strings.SysInfo_GpuIntegrated;
        if (Has("radeon") && (Has("vega") || Has("graphics")) && !Has("rx"))
            return Strings.SysInfo_GpuIntegrated;
        if (Has("microsoft") || Has("basic display")) return Strings.SysInfo_GpuBasic;
        return Strings.SysInfo_GpuDiscrete;
    }

    // --- RAM ---------------------------------------------------------------

    private static string GetRam()
    {
        try
        {
            ulong totalBytes = 0;
            var type = "";
            using (var cs = MakeSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                Wmi.ForEach(cs, mo =>
                {
                    totalBytes = Convert.ToUInt64(mo["TotalPhysicalMemory"] ?? 0UL, CultureInfo.InvariantCulture);
                    return true;
                });
            }
            using (var pm = MakeSearcher(
                "SELECT SMBIOSMemoryType, MemoryType FROM Win32_PhysicalMemory"))
            {
                Wmi.ForEach(pm, mo =>
                {
                    type = MemoryTypeName(mo["SMBIOSMemoryType"], mo["MemoryType"]);
                    return string.IsNullOrEmpty(type); // stop at the first module that reports a type
                });
            }
            var gb = totalBytes / 1024d / 1024d / 1024d;
            var size = string.Format(CultureInfo.CurrentCulture, Strings.Unit_Gigabytes, Math.Round(gb));
            return string.IsNullOrEmpty(type) ? size : $"{size} {type}";
        }
        catch (Exception ex)
        {
            LogWmiError("RAM", ex);
            return "—";
        }
    }

    private static string MemoryTypeName(object? smbios, object? legacy)
    {
        // SMBIOSMemoryType is the reliable modern field.
        var name = ParseCode(smbios) switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            _ => ""
        };
        if (!string.IsNullOrEmpty(name)) return name;

        return ParseCode(legacy) switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", _ => "" };
    }

    /// <summary>WMI numeric property → int; 0 when absent or not a number.</summary>
    private static int ParseCode(object? value)
        => value != null && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            ? code
            : 0;

    // --- Disks -------------------------------------------------------------

    private static string GetDisks()
    {
        try
        {
            ulong total = 0;
            using var searcher = MakeSearcher(
                "SELECT Size FROM Win32_DiskDrive");
            Wmi.ForEach(searcher, mo =>
            {
                total += Convert.ToUInt64(mo["Size"] ?? 0UL, CultureInfo.InvariantCulture);
                return true;
            });

            var tb = total / 1024d / 1024d / 1024d / 1024d;
            return tb >= 1
                ? string.Format(CultureInfo.CurrentCulture, Strings.SysInfo_DiskTotal,
                    string.Format(CultureInfo.CurrentCulture, Strings.Unit_Terabytes, tb))
                : string.Format(CultureInfo.CurrentCulture, Strings.SysInfo_DiskTotal,
                    string.Format(CultureInfo.CurrentCulture, Strings.Unit_Gigabytes, total / 1024d / 1024d / 1024d));
        }
        catch (Exception ex)
        {
            LogWmiError("Disks", ex);
            return "—";
        }
    }

    // --- Windows version ---------------------------------------------------

    private static string GetWindowsVersion()
    {
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var product = RegistryHelper.ReadString(RegHive.LocalMachine, key, "ProductName") ?? "Windows";
        var display = RegistryHelper.ReadString(RegHive.LocalMachine, key, "DisplayVersion"); // 22H2
        var build = RegistryHelper.ReadString(RegHive.LocalMachine, key, "CurrentBuild") ?? "";
        var ubrSnap = RegistryHelper.Capture(RegHive.LocalMachine, key, "UBR");
        var ubr = ubrSnap.Existed ? ubrSnap.Value?.ToString() : null;

        // Windows 11 keeps ProductName = "Windows 10 ..." in the registry, so fix
        // the label based on the build number (>= 22000 is Windows 11).
        if (int.TryParse(build, out var b) && b >= 22000)
            product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);

        var buildFull = string.IsNullOrEmpty(ubr) ? build : $"{build}.{ubr}";
        var displayPart = string.IsNullOrWhiteSpace(display) ? "" : $" {display}";
        // e.g. "Windows 10 Pro 22H2 сборка 19045.6456" / "... build 19045.6456"
        return string.Format(CultureInfo.CurrentCulture, Strings.SysInfo_WindowsBuild,
            $"{product}{displayPart}", buildFull).Trim();
    }

    // --- WMI helpers -------------------------------------------------------

    /// <summary>WMI enumeration timeout. Keeps a slow/hung provider from
    /// stalling the (background) info gather indefinitely.</summary>
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Build a searcher configured for semisynchronous enumeration with a hard
    /// timeout, so <c>Get()</c> gives up instead of blocking forever.
    /// </summary>
    private static ManagementObjectSearcher MakeSearcher(string query)
    {
        return new ManagementObjectSearcher(new ObjectQuery(query)) { Options = Wmi.Options(WmiTimeout) };
    }

    private static string? WmiFirst(string wmiClass, string property)
    {
        try
        {
            using var searcher = MakeSearcher($"SELECT {property} FROM {wmiClass}");
            string? found = null;
            Wmi.ForEach(searcher, mo =>
            {
                var v = mo[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) found = v.Trim();
                return found is null;
            });
            if (found != null) return found;
        }
        catch (Exception ex)
        {
            LogWmiError($"WMI {wmiClass}.{property}", ex);
        }
        return null;
    }

    /// <summary>Log a WMI failure, flagging access-denied explicitly.</summary>
    private static void LogWmiError(string action, Exception ex)
    {
        var denied = ex is UnauthorizedAccessException
            || (ex is ManagementException me &&
                me.ErrorCode is ManagementStatus.AccessDenied);
        if (denied) Logger.Log(action, "ACCESS_DENIED", ex.Message);
        else Logger.LogError(action, ex);
    }

    public void Dispose()
    {
        try { lock (_lhmGate) { _computer?.Close(); _computer = null; } }
        catch { /* ignore */ }
    }
}

/// <summary>Visitor that refreshes hardware and sub-hardware sensor values.</summary>
internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) sub.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
