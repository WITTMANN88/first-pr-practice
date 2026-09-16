using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Browsers;

/// <summary>
/// Деблоат Edge без удаления — подтверждено официальной документацией политик Edge
/// (learn.microsoft.com/deployedge/microsoft-edge-policies): HKLM\SOFTWARE\Policies\Microsoft\Edge —
/// HubsSidebarEnabled (Sidebar), EdgeShoppingAssistantEnabled (Shopping Assistant),
/// Microsoft365CopilotChatIconEnabled (значок Copilot в панели инструментов). Все три — политики
/// вида "...Enabled" (0=выключено), поэтому Invert=true в отличие от Brave. Политика телеметрии
/// Edge не подтверждена в этом заходе — не включена сюда, чтобы не гадать про реестровый ключ.
/// </summary>
public static class EdgeDebloatHandler
{
    private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Edge";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "HubsSidebarEnabled", Invert: true),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "EdgeShoppingAssistantEnabled", Invert: true),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "Microsoft365CopilotChatIconEnabled", Invert: true)
    });
}
