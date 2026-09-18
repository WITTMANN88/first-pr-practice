using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Clean;

/// <summary>
/// Storage Sense — общий тумблер вкл/выкл. Значение "01" (DWORD, буквально строка с ведущим
/// нулём как имя параметра) под StorageSense\Parameters\StoragePolicy — широко задокументированный
/// в сообществе твикеров формат (совпадает у нескольких независимых источников), но не
/// зафиксирован в официальной документации Microsoft — тот же уровень уверенности, что и у
/// Get-WindowsReservedStorageState (см. ReservedStorageHandler), не проверено вживую в этой сессии.
/// План честно отмечал только префикс пути без точного имени параметра — это и есть найденное имя.
/// </summary>
public static class StorageSenseHandler
{
    private const string SubPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.CurrentUser, SubPath, "01", OnValue: 1, OffValue: 0)
    });
}
