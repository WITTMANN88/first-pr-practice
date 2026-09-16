using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Privacy;

/// <summary>
/// Веб-результаты в поиске Windows выкл. — HKCU\Software\Microsoft\Windows\CurrentVersion\Search:
/// BingSearchEnabled, CortanaConsent (DWORD, 1=включено). Один из самых частых кандидатов
/// на откат Windows Update — хороший тестовый случай для «Проверить состояние» на Главной.
/// </summary>
public static class WebSearchHandler
{
    private const string PolicyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.CurrentUser, PolicyPath, "BingSearchEnabled", OnValue: 0, OffValue: 1),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.CurrentUser, PolicyPath, "CortanaConsent", OnValue: 0, OffValue: 1)
    });
}
