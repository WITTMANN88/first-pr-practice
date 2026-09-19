using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Privacy;

/// <summary>
/// Отключить Windows Copilot — TurnOffWindowsCopilot=1 под HKCU и продублировано под
/// HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot (план явно советует дублировать
/// для надёжности на части сборок). Имя ключа само говорит о смысле значения (1=выключено),
/// без инверсии.
/// </summary>
public static class WindowsCopilotHandler
{
    private const string SubPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.CurrentUser, SubPath, "TurnOffWindowsCopilot", OnValue: 1, OffValue: 0),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, SubPath, "TurnOffWindowsCopilot", OnValue: 1, OffValue: 0)
    });
}
