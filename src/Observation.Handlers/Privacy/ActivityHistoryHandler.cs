using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Privacy;

/// <summary>
/// Журнал действий (Activity History) выкл. — три политики под
/// HKLM\SOFTWARE\Policies\Microsoft\Windows\System: EnableActivityFeed,
/// PublishUserActivities, UploadUserActivities (DWORD, 1=включено).
/// </summary>
public static class ActivityHistoryHandler
{
    private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\System";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "EnableActivityFeed", OnValue: 0, OffValue: 1),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "PublishUserActivities", OnValue: 0, OffValue: 1),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "UploadUserActivities", OnValue: 0, OffValue: 1)
    });
}
