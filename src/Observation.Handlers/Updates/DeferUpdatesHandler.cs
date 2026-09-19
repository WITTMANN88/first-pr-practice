using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Updates;

/// <summary>
/// Отложить обновления функций (~365 дней) и качества (~4 дня) — периоды без булевых
/// флагов DeferFeatureUpdates/DeferQualityUpdates игнорируются, поэтому все четыре значения
/// идут одним твиком (см. план, «Вкладка 12» техническая таблица).
/// HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate.
/// </summary>
public static class DeferUpdatesHandler
{
    private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "DeferFeatureUpdates", OnValue: 1, OffValue: 0),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "DeferFeatureUpdatesPeriodInDays", OnValue: 365, OffValue: 0),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "DeferQualityUpdates", OnValue: 1, OffValue: 0),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "DeferQualityUpdatesPeriodInDays", OnValue: 4, OffValue: 0)
    });
}
