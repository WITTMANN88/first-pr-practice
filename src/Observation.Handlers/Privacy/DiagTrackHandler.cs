using Observation.Core.SystemAccess;

namespace Observation.Handlers.Privacy;

/// <summary>
/// Отключение телеметрии (DiagTrack) — из плана: политика AllowTelemetry=0 плюс сама служба
/// в ручной/отключённый режим и остановлена (`Set-Service DiagTrack -StartupType Disabled;
/// Stop-Service DiagTrack`), не только реестр — работающая служба сама по себе не остановится
/// от одной политики. Best-effort: обратная сторона — это отдельная forward-команда
/// (Automatic + Start-Service), а не гарантированный возврат к состоянию "до".
/// </summary>
public static class DiagTrackHandler
{
    private const string PolicyKey = @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection";

    public static PowerShellToggleHandler Create(ICommandRunner runner) => new(
        runner,
        onScript: $"Set-ItemProperty -Path '{PolicyKey}' -Name AllowTelemetry -Value 0 -Type DWord -Force; " +
                  "Set-Service DiagTrack -StartupType Disabled; Stop-Service DiagTrack -Force",
        offScript: $"Remove-ItemProperty -Path '{PolicyKey}' -Name AllowTelemetry -ErrorAction SilentlyContinue; " +
                   "Set-Service DiagTrack -StartupType Automatic; Start-Service DiagTrack",
        verifyScript: "(Get-Service DiagTrack).StartType -eq 'Disabled'");
}
