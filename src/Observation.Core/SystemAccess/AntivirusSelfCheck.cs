using Microsoft.Win32;

namespace Observation.Core.SystemAccess;

/// <summary>
/// Механизм надёжности №5 из плана: лёгкая проба записи в реестр + проверка прав на запись
/// в папку данных — при старте, после получения admin-прав.
/// </summary>
public static class AntivirusSelfCheck
{
    private const string SelfTestPath = @"Software\Observation\_selftest";
    private const string SelfTestValue = "probe";

    public static SelfCheckResult Run(IRegistryAccessor registry, string dataFolderPath)
    {
        var registryBlocked = !TryRegistryProbe(registry);
        var dataFolderBlocked = !TryDataFolderProbe(dataFolderPath);
        return new SelfCheckResult(registryBlocked, dataFolderBlocked);
    }

    private static bool TryRegistryProbe(IRegistryAccessor registry)
    {
        try
        {
            registry.WriteValue(RegistryHive.CurrentUser, SelfTestPath, SelfTestValue, 1, RegistryValueKind.DWord);
            var ok = registry.TryReadValue(RegistryHive.CurrentUser, SelfTestPath, SelfTestValue, out _, out _);
            registry.DeleteValue(RegistryHive.CurrentUser, SelfTestPath, SelfTestValue);
            return ok;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryDataFolderProbe(string dataFolderPath)
    {
        try
        {
            Directory.CreateDirectory(dataFolderPath);
            var probeFile = Path.Combine(dataFolderPath, ".selftest");
            File.WriteAllText(probeFile, string.Empty);
            File.Delete(probeFile);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>RegistryBlocked/DataFolderBlocked=true — повод показать баннер про антивирус/CFA (см. план).</summary>
public sealed record SelfCheckResult(bool RegistryBlocked, bool DataFolderBlocked)
{
    public bool IsHealthy => !RegistryBlocked && !DataFolderBlocked;
}
