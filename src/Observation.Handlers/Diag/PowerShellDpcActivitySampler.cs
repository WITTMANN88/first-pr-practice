using System.Text.Json;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Diag;

public interface IDpcActivitySampler
{
    Task<DpcActivitySample> SampleAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// План изначально хотел «DPC/ISR-задержку» через обёртку над wpr.exe (Windows Performance
/// Recorder) — но wpr.exe только записывает трассировку (.etl), число из неё вытаскивает
/// отдельный анализ в WPA (Windows Performance Analyzer) или разбор XML-экспорта — ни то,
/// ни другое не даёт одну готовую цифру «задержка в мкс» без полноценного UI анализа,
/// непропорционально сложно для одной диагностической карточки. Честная замена — те же
/// официальные счётчики производительности Windows, что показывает Resource Monitor
/// (Get-Counter '% DPC Time' / '% Interrupt Time'): не микросекундная задержка, а процент
/// времени CPU, потраченного на DPC/прерывания за последнюю секунду — тоже реальный,
/// официально документированный показатель нагрузки, просто другой по единицам измерения.
/// UI должен подписывать его соответствующе честно (см. DiagView.xaml), а не выдавать за то,
/// что показывает LatencyMon.
/// </summary>
public sealed class PowerShellDpcActivitySampler : IDpcActivitySampler
{
    private readonly ICommandRunner _runner;

    public PowerShellDpcActivitySampler(ICommandRunner runner) => _runner = runner;

    public async Task<DpcActivitySample> SampleAsync(CancellationToken cancellationToken = default)
    {
        const string script =
            "(Get-Counter '\\Processor Information(_Total)\\% DPC Time','\\Processor Information(_Total)\\% Interrupt Time' " +
            "-SampleInterval 1 -MaxSamples 1).CounterSamples | Select-Object Path, CookedValue | ConvertTo-Json -Compress";

        var result = await _runner.RunAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
            throw new InvalidOperationException($"Get-Counter завершился с ошибкой: {result.StandardError}");

        return ParseSample(result.StandardOutput);
    }

    /// <summary>Как и другие PowerShell-сканеры в проекте: Windows PowerShell 5.1 не оборачивает единственный результат в массив.</summary>
    public static DpcActivitySample ParseSample(string json)
    {
        json = json.Trim();
        if (json.Length == 0)
            return new DpcActivitySample(0, 0);

        if (!json.StartsWith('['))
            json = $"[{json}]";

        double dpc = 0, interrupt = 0;
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var path = element.TryGetProperty("Path", out var p) ? p.GetString() ?? "" : "";
            var value = element.TryGetProperty("CookedValue", out var v) ? v.GetDouble() : 0;

            if (path.Contains("dpc", StringComparison.OrdinalIgnoreCase))
                dpc = value;
            else if (path.Contains("interrupt", StringComparison.OrdinalIgnoreCase))
                interrupt = value;
        }

        return new DpcActivitySample(dpc, interrupt);
    }
}
