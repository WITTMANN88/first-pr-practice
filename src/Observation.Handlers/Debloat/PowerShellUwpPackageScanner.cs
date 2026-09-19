using System.Text.Json;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Debloat;

public interface IUwpPackageScanner
{
    Task<IReadOnlyList<UwpPackageInfo>> ScanAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> RemoveAsync(string packageFullName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Сканирование реально установленных UWP-пакетов (вкладка 5, «Сканирование UWP-приложений»
/// в плане) — план называет нативный WinRT PackageManager как механизм, но у него нет прямого
/// .NET API без CsWinRT-проекции (лишняя зависимость + WinRT-специфичный рантайм, который
/// нельзя даже собрать-проверить в песочнице этой сессии, только на реальной машине);
/// Get-AppxPackage через уже готовый ICommandRunner даёт те же данные (реальный список
/// установленных пакетов конкретного ПК, не захардкоженные 4-5 примеров) с тем же уровнем
/// системного доступа, которым уже пользуются командные твики.
/// -AllUsers — та же логика «применяется ко всем локальным профилям», что и у остального
/// деблоата (см. план, «Применение ко всем профилям»).
/// </summary>
public sealed class PowerShellUwpPackageScanner : IUwpPackageScanner
{
    // Неприкасаемый список из плана — исключаем эти пакеты даже если WinRT/PowerShell сами
    // не пометили их как IsFramework/NonRemovable.
    private static readonly string[] AlwaysHiddenNames =
    {
        "Microsoft.DesktopAppInstaller",
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.WindowsStore",
        "Microsoft.Windows.StartMenuExperienceHost"
    };

    private readonly ICommandRunner _runner;

    public PowerShellUwpPackageScanner(ICommandRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<UwpPackageInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        const string script =
            "Get-AppxPackage -AllUsers | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | " +
            "Select-Object Name, PackageFullName, Publisher | ConvertTo-Json -Compress";

        var result = await RunPowerShellAsync(script, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Get-AppxPackage завершился с ошибкой: {result.StandardError}");

        return ParsePackages(result.StandardOutput);
    }

    public Task<CommandResult> RemoveAsync(string packageFullName, CancellationToken cancellationToken = default)
    {
        var escaped = packageFullName.Replace("'", "''");
        var script = $"Remove-AppxPackage -Package '{escaped}' -AllUsers -ErrorAction Stop";
        return RunPowerShellAsync(script, cancellationToken);
    }

    /// <summary>Windows PowerShell 5.1 ConvertTo-Json не оборачивает единственный результат в массив (в отличие от pwsh 7+ с -AsArray) — нормализуем форму здесь, а не полагаемся на версию PowerShell на машине пользователя.</summary>
    public static IReadOnlyList<UwpPackageInfo> ParsePackages(string json)
    {
        json = json.Trim();
        if (json.Length == 0)
            return Array.Empty<UwpPackageInfo>();

        if (!json.StartsWith('['))
            json = $"[{json}]";

        using var doc = JsonDocument.Parse(json);
        var packages = new List<UwpPackageInfo>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var name = element.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
            var fullName = element.TryGetProperty("PackageFullName", out var f) ? f.GetString() ?? "" : "";
            var publisher = element.TryGetProperty("Publisher", out var p) ? p.GetString() ?? "" : "";

            if (fullName.Length == 0 || AlwaysHiddenNames.Any(hidden => name.Equals(hidden, StringComparison.OrdinalIgnoreCase)))
                continue;

            packages.Add(new UwpPackageInfo(fullName, name, publisher));
        }

        return packages;
    }

    private Task<CommandResult> RunPowerShellAsync(string script, CancellationToken cancellationToken) =>
        _runner.RunAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            cancellationToken);
}
