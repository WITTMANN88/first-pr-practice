using System.Text.Json;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Diag;

public interface IProblemDeviceScanner
{
    Task<IReadOnlyList<ProblemDeviceInfo>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Устройства с ошибкой в диспетчере устройств — Win32_PNPEntity.ConfigManagerErrorCode отличен
/// от 0 (0 = "устройство работает нормально", тот же признак, что показывает жёлтый треугольник
/// в devmgmt.msc). Только чтение, ничего не исправляет — вкладка «Диагностика» read-only
/// по этой части плана, чинить проблемное устройство пользователь должен сам (отсюда кнопка
/// «искать в интернете» рядом с каждым, а не «исправить»).
/// </summary>
public sealed class PowerShellProblemDeviceScanner : IProblemDeviceScanner
{
    private readonly ICommandRunner _runner;

    public PowerShellProblemDeviceScanner(ICommandRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<ProblemDeviceInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        const string script =
            "Get-CimInstance Win32_PNPEntity | Where-Object { $_.ConfigManagerErrorCode -ne 0 } | " +
            "Select-Object Name, ConfigManagerErrorCode | ConvertTo-Json -Compress";

        var result = await _runner.RunAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
            throw new InvalidOperationException($"Get-CimInstance Win32_PNPEntity завершился с ошибкой: {result.StandardError}");

        return ParseDevices(result.StandardOutput);
    }

    /// <summary>Как и в PowerShellUwpPackageScanner: Windows PowerShell 5.1 не оборачивает единственный результат в массив.</summary>
    public static IReadOnlyList<ProblemDeviceInfo> ParseDevices(string json)
    {
        json = json.Trim();
        if (json.Length == 0)
            return Array.Empty<ProblemDeviceInfo>();

        if (!json.StartsWith('['))
            json = $"[{json}]";

        using var doc = JsonDocument.Parse(json);
        var devices = new List<ProblemDeviceInfo>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var name = element.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
            var errorCode = element.TryGetProperty("ConfigManagerErrorCode", out var e) && e.TryGetInt32(out var code) ? code : 0;

            if (name.Length == 0)
                continue;

            devices.Add(new ProblemDeviceInfo(name, errorCode));
        }

        return devices;
    }
}
