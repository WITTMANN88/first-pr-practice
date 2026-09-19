using Observation.Core.SystemAccess;

namespace Observation.Handlers.Clean;

/// <summary>
/// Reserved Storage — DISM /Online /Set-ReservedStorageState (см. tweaks.sample.json,
/// clean.reservedstorage). Верификация через Get-WindowsReservedStorageState — реальный
/// командлет Windows 10/11, но не проверен вживую в этой сессии (нет доступа к Windows
/// в песочнице), помечено честно и в описании твика.
/// </summary>
public static class ReservedStorageHandler
{
    public static PowerShellToggleHandler Create(ICommandRunner runner) => new(
        runner,
        onScript: "DISM /Online /Set-ReservedStorageState /State:Disabled",
        offScript: "DISM /Online /Set-ReservedStorageState /State:Enabled",
        verifyScript: "(Get-WindowsReservedStorageState) -eq 'Disabled'");
}
