using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Browsers;

/// <summary>
/// Деблоат Brave без удаления — три политики, подтверждённые в плане:
/// HKLM\SOFTWARE\Policies\BraveSoftware\Brave — BraveRewardsDisabled/BraveWalletDisabled/
/// BraveVPNDisabled (DWORD, 1=выключено). Остальные политики Brave (например AI-чат) —
/// не подтверждены в этом заходе, добавляются позже по официальному списку Brave.
/// </summary>
public static class BraveDebloatHandler
{
    private const string PolicyPath = @"SOFTWARE\Policies\BraveSoftware\Brave";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "BraveRewardsDisabled"),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "BraveWalletDisabled"),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "BraveVPNDisabled")
    });
}
