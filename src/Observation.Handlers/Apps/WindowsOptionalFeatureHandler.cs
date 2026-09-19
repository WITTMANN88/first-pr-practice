using Observation.Core.SystemAccess;

namespace Observation.Handlers.Apps;

/// <summary>
/// Компоненты Windows (Windows Optional Features) — вкладка 8, «включение системных
/// компонентов одной кнопкой» из плана: Enable-WindowsOptionalFeature/Disable-WindowsOptionalFeature,
/// проверка через Get-WindowsOptionalFeature. -NoRestart — сама фича может требовать
/// перезагрузку для полного вступления в силу (см. requiresReboot твика в JSON), но
/// не должна форсировать её посреди выполнения самой команды.
/// </summary>
public static class WindowsOptionalFeatureHandler
{
    public static PowerShellToggleHandler Create(ICommandRunner runner, string featureName) => new(
        runner,
        onScript: $"Enable-WindowsOptionalFeature -Online -FeatureName {featureName} -All -NoRestart -ErrorAction Stop | Out-Null",
        offScript: $"Disable-WindowsOptionalFeature -Online -FeatureName {featureName} -NoRestart -ErrorAction Stop | Out-Null",
        verifyScript: $"(Get-WindowsOptionalFeature -Online -FeatureName {featureName}).State -eq 'Enabled'");
}
