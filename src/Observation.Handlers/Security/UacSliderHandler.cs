using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Security;

/// <summary>
/// UAC-слайдер на минимум («никогда не уведомлять») — риск, никогда не в пресетах.
/// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System:
/// ConsentPromptBehaviorAdmin=0 (elevate without prompting) и PromptOnSecureDesktop=0
/// (не на защищённом рабочем столе). Windows-значения по умолчанию — 5 и 1 соответственно
/// (подтверждено через официальную документацию UAC-политик), используются как OffValue,
/// не наугад. Сама служба UAC остаётся включённой — только запрос повышения для админов
/// перестаёт появляться; не косметика, реальное снижение защиты.
/// </summary>
public static class UacSliderHandler
{
    private const string PolicyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "ConsentPromptBehaviorAdmin", OnValue: 0, OffValue: 5),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.LocalMachine, PolicyPath, "PromptOnSecureDesktop", OnValue: 0, OffValue: 1)
    });
}
