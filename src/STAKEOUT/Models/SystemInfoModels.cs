namespace Stakeout.Models;

/// <summary>One GPU row (integrated or discrete shown separately).</summary>
public sealed class GpuInfo
{
    public string Name { get; set; } = "—";
    public string Kind { get; set; } = "";   // "Встроенная" / "Дискретная"
}

/// <summary>Snapshot of system information for the home page.</summary>
public sealed class SystemInfoModel
{
    public string CpuName { get; set; } = "—";
    /// <summary>Package/core temperature in °C. Null when no sensor is available.</summary>
    public double? CpuTemperatureC { get; set; }

    public string Motherboard { get; set; } = "—";

    public List<GpuInfo> Gpus { get; set; } = new();

    public string RamSummary { get; set; } = "—";   // e.g. "32 ГБ DDR4"
    public string DiskSummary { get; set; } = "—";   // e.g. "Всего: 1.9 ТБ"
    public string WindowsVersion { get; set; } = "—"; // full build string
}
