using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Handlers.Perf;

/// <summary>
/// Game Mode — HKCU\Software\Microsoft\GameBar: AutoGameModeEnabled (основной ключ по
/// актуальному справочнику параметров Windows 11) + AllowAutoGameMode (для совместимости
/// со старыми сборками, где основным был он). Изначально твик писал только
/// AllowAutoGameMode — это была иллюстрация схемы твика из более раннего черновика плана;
/// исправлено после сверки с итоговой технической таблицей.
/// </summary>
public static class GameModeHandler
{
    private const string SubPath = @"SOFTWARE\Microsoft\GameBar";

    public static MultiRegistryValueHandler Create(IRegistryAccessor registry) => new(registry, new[]
    {
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.CurrentUser, SubPath, "AutoGameModeEnabled", OnValue: 1, OffValue: 0),
        new MultiRegistryValueHandler.PolicyValue(RegistryHive.CurrentUser, SubPath, "AllowAutoGameMode", OnValue: 1, OffValue: 0)
    });
}
