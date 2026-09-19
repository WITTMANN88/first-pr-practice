using System.Management;

namespace Observation.Core.SystemAccess;

/// <summary>
/// Характеристики ПК для карточки на «Главной» — из плана: Get-CimInstance
/// Win32_OperatingSystem / Win32_Processor / Win32_VideoController / Win32_PhysicalMemory /
/// Win32_LogicalDisk. Только чтение, без побочных эффектов. Отказ по отдельному запросу —
/// не критично, просто показываем "неизвестно" для этого поля.
/// </summary>
public sealed class PcSpecsProvider
{
    public PcSpecs GetCurrent() => new(
        OsCaption: QuerySingle("SELECT Caption FROM Win32_OperatingSystem", "Caption") ?? "неизвестно",
        CpuName: QuerySingle("SELECT Name FROM Win32_Processor", "Name") ?? "неизвестно",
        GpuName: QuerySingle("SELECT Name FROM Win32_VideoController", "Name") ?? "неизвестно",
        RamTotal: FormatRamTotal(),
        DiskInfo: FormatDiskInfo());

    private static string? QuerySingle(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementBaseObject item in searcher.Get())
                return item[property] as string;
        }
        catch (Exception)
        {
            // WMI недоступен/заблокирован — поле останется "неизвестно", это не критично для дашборда.
        }

        return null;
    }

    private static string FormatRamTotal()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            long totalBytes = 0;
            foreach (ManagementBaseObject item in searcher.Get())
                totalBytes += Convert.ToInt64(item["Capacity"]);

            return totalBytes > 0 ? $"{totalBytes / (1024.0 * 1024 * 1024):0.#} ГБ" : "неизвестно";
        }
        catch (Exception)
        {
            return "неизвестно";
        }
    }

    /// <summary>
    /// Раньше показывал только раздел C: — терял и общий физический объём накопителя(ей), и
    /// остальные разделы (D:, E: и т.д.). Теперь: Win32_DiskDrive.Size — суммарная физическая
    /// ёмкость всех физических накопителей (не логических разделов — это разные числа, если
    /// есть неразмеченное место/скрытые служебные разделы); Win32_LogicalDisk WHERE DriveType=3
    /// (только локальные несъёмные разделы, не CD-ROM/сетевые/съёмные диски) — перечисление всех
    /// логических разделов с их занятостью, не только системного.
    /// </summary>
    private static string FormatDiskInfo()
    {
        long totalPhysicalBytes = 0;
        try
        {
            using var driveSearcher = new ManagementObjectSearcher("SELECT Size FROM Win32_DiskDrive");
            foreach (ManagementBaseObject drive in driveSearcher.Get())
                totalPhysicalBytes += Convert.ToInt64(drive["Size"]);
        }
        catch (Exception)
        {
            // Общий физический объём останется неизвестным — разбивка по разделам ниже всё равно покажется.
        }

        var partitions = new List<string>();
        try
        {
            using var logicalSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (ManagementBaseObject disk in logicalSearcher.Get())
            {
                var deviceId = disk["DeviceID"] as string ?? "?:";
                var size = Convert.ToInt64(disk["Size"]);
                var free = Convert.ToInt64(disk["FreeSpace"]);
                partitions.Add($"{deviceId} {FormatBytes(free)} своб. из {FormatBytes(size)}");
            }
        }
        catch (Exception)
        {
            // см. QuerySingle
        }

        if (partitions.Count == 0)
            return "неизвестно";

        var header = totalPhysicalBytes > 0 ? $"{FormatBytes(totalPhysicalBytes)} физически" : "объём накопителя неизвестен";
        return header + "\n" + string.Join("\n", partitions.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }

    private static string FormatBytes(long bytes)
    {
        var gb = bytes / (1024.0 * 1024 * 1024);
        return gb >= 1000 ? $"{gb / 1024.0:0.##} ТБ" : $"{gb:0.#} ГБ";
    }
}
