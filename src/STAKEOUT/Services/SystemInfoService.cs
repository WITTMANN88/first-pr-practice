using System.Management;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using Stakeout.Core;
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
        var model = new SystemInfoModel();
        model.CpuName = GetCpuName();
        model.CpuTemperatureC = GetCpuTemperature();
        model.Motherboard = GetMotherboard();
        model.Gpus = GetGpus();
        model.RamSummary = GetRam();
        model.DiskSummary = GetDisks();
        model.WindowsVersion = GetWindowsVersion();
        return model;
    });

    // --- CPU ---------------------------------------------------------------

    private static string GetCpuName()
    {
        // Registry is the fastest, most reliable name source.
        var name = RegistryHelper.ReadString(RegistryHive.LocalMachine,
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
        if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();

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
                            package = v;
                        else
                            coreTemps.Add(v);
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

    private Computer OpenComputer()
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

        var biosBoard = RegistryHelper.ReadString(RegistryHive.LocalMachine,
            @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct");
        return string.IsNullOrWhiteSpace(biosBoard) ? "—" : biosBoard!.Trim();
    }

    // --- GPUs --------------------------------------------------------------

    private static List<GpuInfo> GetGpus()
    {
        var list = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterCompatibility FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new GpuInfo { Name = name!.Trim(), Kind = ClassifyGpu(name!) });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("GPU enumeration", ex);
        }
        if (list.Count == 0) list.Add(new GpuInfo { Name = "—" });
        return list;
    }

    /// <summary>Heuristic: Intel/AMD APU graphics are integrated, the rest discrete.</summary>
    private static string ClassifyGpu(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("intel") && (n.Contains("uhd") || n.Contains("hd graphics") || n.Contains("iris")))
            return "Встроенная";
        if (n.Contains("radeon") && (n.Contains("vega") || n.Contains("graphics")) && !n.Contains("rx"))
            return "Встроенная";
        if (n.Contains("microsoft") || n.Contains("basic display")) return "Базовая";
        return "Дискретная";
    }

    // --- RAM ---------------------------------------------------------------

    private static string GetRam()
    {
        try
        {
            ulong totalBytes = 0;
            var type = "";
            using (var cs = new ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (ManagementObject mo in cs.Get())
                    totalBytes = Convert.ToUInt64(mo["TotalPhysicalMemory"] ?? 0UL);
            }
            using (var pm = new ManagementObjectSearcher(
                "SELECT SMBIOSMemoryType, MemoryType FROM Win32_PhysicalMemory"))
            {
                foreach (ManagementObject mo in pm.Get())
                {
                    type = MemoryTypeName(mo["SMBIOSMemoryType"], mo["MemoryType"]);
                    if (!string.IsNullOrEmpty(type)) break;
                }
            }
            var gb = totalBytes / 1024d / 1024d / 1024d;
            return $"{Math.Round(gb)} ГБ{(string.IsNullOrEmpty(type) ? "" : " " + type)}";
        }
        catch (Exception ex)
        {
            Logger.LogError("RAM", ex);
            return "—";
        }
    }

    private static string MemoryTypeName(object? smbios, object? legacy)
    {
        // SMBIOSMemoryType is the reliable modern field.
        var code = 0;
        if (smbios != null) int.TryParse(smbios.ToString(), out code);
        var name = code switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            _ => ""
        };
        if (!string.IsNullOrEmpty(name)) return name;

        var lcode = 0;
        if (legacy != null) int.TryParse(legacy.ToString(), out lcode);
        return lcode switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", _ => "" };
    }

    // --- Disks -------------------------------------------------------------

    private static string GetDisks()
    {
        try
        {
            ulong total = 0;
            using var searcher = new ManagementObjectSearcher(
                "SELECT Size FROM Win32_DiskDrive");
            foreach (ManagementObject mo in searcher.Get())
                total += Convert.ToUInt64(mo["Size"] ?? 0UL);

            var tb = total / 1024d / 1024d / 1024d / 1024d;
            return tb >= 1
                ? $"Всего: {Math.Round(tb, 2)} ТБ"
                : $"Всего: {Math.Round(total / 1024d / 1024d / 1024d)} ГБ";
        }
        catch (Exception ex)
        {
            Logger.LogError("Disks", ex);
            return "—";
        }
    }

    // --- Windows version ---------------------------------------------------

    private static string GetWindowsVersion()
    {
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var product = RegistryHelper.ReadString(RegistryHive.LocalMachine, key, "ProductName") ?? "Windows";
        var display = RegistryHelper.ReadString(RegistryHive.LocalMachine, key, "DisplayVersion"); // 22H2
        var build = RegistryHelper.ReadString(RegistryHive.LocalMachine, key, "CurrentBuild") ?? "";
        var ubrSnap = RegistryHelper.Capture(RegistryHive.LocalMachine, key, "UBR");
        var ubr = ubrSnap.Existed ? ubrSnap.Value?.ToString() : null;

        // Windows 11 keeps ProductName = "Windows 10 ..." in the registry, so fix
        // the label based on the build number (>= 22000 is Windows 11).
        if (int.TryParse(build, out var b) && b >= 22000)
            product = product.Replace("Windows 10", "Windows 11");

        var buildFull = string.IsNullOrEmpty(ubr) ? build : $"{build}.{ubr}";
        var displayPart = string.IsNullOrWhiteSpace(display) ? "" : $" {display}";
        // e.g. "Windows 10 Pro 22H2 сборка 19045.6456"
        return $"{product}{displayPart} сборка {buildFull}".Trim();
    }

    // --- WMI helper --------------------------------------------------------

    private static string? WmiFirst(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (ManagementObject mo in searcher.Get())
            {
                var v = mo[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"WMI {wmiClass}.{property}", ex);
        }
        return null;
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
