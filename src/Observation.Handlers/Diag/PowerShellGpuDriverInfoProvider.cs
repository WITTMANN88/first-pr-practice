using System.Text.Json;
using System.Text.RegularExpressions;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Diag;

public interface IGpuDriverInfoProvider
{
    Task<IReadOnlyList<GpuDriverInfo>> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Версия/дата драйвера видеоадаптера (план, «Поиск устаревших/недостающих драйверов по
/// PnP ID» — здесь упрощено до видеокарты конкретно, самого частого кандидата на «устаревший
/// драйвер»; без общей базы «текущая последняя версия по PnP ID» — её у нас нет и заводить
/// в личном инструменте непропорционально, вместо неё — прямая ссылка на страницу загрузок
/// вендора, «свежее или нет» решает сам пользователь, как и было заявлено в плане: «без автоустановки»).
/// </summary>
public sealed class PowerShellGpuDriverInfoProvider : IGpuDriverInfoProvider
{
    private readonly ICommandRunner _runner;

    public PowerShellGpuDriverInfoProvider(ICommandRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<GpuDriverInfo>> GetAsync(CancellationToken cancellationToken = default)
    {
        const string script =
            "Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, DriverDate, AdapterCompatibility | ConvertTo-Json -Compress";

        var result = await _runner.RunAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
            throw new InvalidOperationException($"Get-CimInstance Win32_VideoController завершился с ошибкой: {result.StandardError}");

        return ParseDrivers(result.StandardOutput);
    }

    public static IReadOnlyList<GpuDriverInfo> ParseDrivers(string json)
    {
        json = json.Trim();
        if (json.Length == 0)
            return Array.Empty<GpuDriverInfo>();

        if (!json.StartsWith('['))
            json = $"[{json}]";

        var drivers = new List<GpuDriverInfo>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var name = element.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
            var version = element.TryGetProperty("DriverVersion", out var v) ? v.GetString() ?? "" : "";
            var rawDate = element.TryGetProperty("DriverDate", out var d) ? d.GetString() ?? "" : "";
            var vendor = element.TryGetProperty("AdapterCompatibility", out var a) ? a.GetString() ?? "" : "";

            if (name.Length == 0)
                continue;

            drivers.Add(new GpuDriverInfo(name, version, FormatCimDate(rawDate), vendor));
        }

        return drivers;
    }

    private static readonly Regex JsonDateRegex = new(@"^/Date\((-?\d+)\)/$", RegexOptions.Compiled);

    /// <summary>
    /// Get-CimInstance превращает CIM_DATETIME в System.DateTime, поэтому ConvertTo-Json отдаёт его
    /// в формате "/Date(unixMs)/" (не сырую CIM-строку "20230815000000.000000-000", несмотря на то,
    /// что PowerShell мог бы отдать и её) — отсюда явный разбор обоих форматов.
    /// </summary>
    private static string FormatCimDate(string raw)
    {
        var jsonDateMatch = JsonDateRegex.Match(raw);
        if (jsonDateMatch.Success && long.TryParse(jsonDateMatch.Groups[1].Value, out var unixMs))
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime.ToString("yyyy-MM-dd");

        return raw.Length >= 8 ? $"{raw[..4]}-{raw[4..6]}-{raw[6..8]}" : raw;
    }
}
