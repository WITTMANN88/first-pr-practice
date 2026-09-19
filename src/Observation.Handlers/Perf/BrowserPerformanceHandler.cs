using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Perf;

/// <summary>
/// Chrome/Edge: аппаратное ускорение и фоновые службы выкл. — HardwareAccelerationModeEnabled
/// и BackgroundModeEnabled под HKLM\SOFTWARE\Policies\Google\Chrome и \Microsoft\Edge
/// (DWORD, 1=включено). Работает даже если браузер сейчас открыт — применяется при
/// следующем запуске. Отдельный твик от «Деблоат Edge» (та же вкладка «Приложения» —
/// про декодированные функции UI, а не производительность) — оба реально независимы.
/// </summary>
public static class BrowserPerformanceHandler
{
    private const string ChromePath = @"SOFTWARE\Policies\Google\Chrome";
    private const string EdgePath = @"SOFTWARE\Policies\Microsoft\Edge";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, ChromePath, "HardwareAccelerationModeEnabled", OnValue: 0, OffValue: 1),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, ChromePath, "BackgroundModeEnabled", OnValue: 0, OffValue: 1),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, EdgePath, "HardwareAccelerationModeEnabled", OnValue: 0, OffValue: 1),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, EdgePath, "BackgroundModeEnabled", OnValue: 0, OffValue: 1)
    });
}
