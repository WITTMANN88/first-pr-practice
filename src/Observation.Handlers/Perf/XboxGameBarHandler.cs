using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Perf;

/// <summary>
/// Xbox Game Bar / Game DVR выкл. — HKCU\System\GameConfigStore\GameDVR_Enabled +
/// HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR (оба DWORD, 1=включено).
/// Исключение для двухчиплетных AMD X3D — на уровне applicability твика (CPU-детект),
/// не здесь: этот обработчик просто пишет оба значения, ничего не знает про модель CPU.
/// </summary>
public static class XboxGameBarHandler
{
    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", OnValue: 0, OffValue: 1),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", OnValue: 0, OffValue: 1)
    });
}
