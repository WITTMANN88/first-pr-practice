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
        DiskInfo: FormatSystemDisk());

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

    private static string FormatSystemDisk()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID='C:'");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                var size = Convert.ToInt64(item["Size"]);
                var free = Convert.ToInt64(item["FreeSpace"]);
                return $"{free / (1024.0 * 1024 * 1024):0.#} ГБ свободно из {size / (1024.0 * 1024 * 1024):0.#} ГБ (C:)";
            }
        }
        catch (Exception)
        {
            // см. QuerySingle
        }

        return "неизвестно";
    }
}
