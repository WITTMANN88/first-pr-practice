using Microsoft.Win32;

namespace Observation.Core.SystemAccess;

/// <summary>Реальный доступ к реестру напрямую из C# — без промежуточного PowerShell-хоста (см. «Исполнение и системный доступ»).</summary>
public sealed class Win32RegistryAccessor : IRegistryAccessor
{
    public bool TryReadValue(RegistryHive hive, string path, string valueName, out object? data, out RegistryValueKind kind)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var subKey = baseKey.OpenSubKey(path, writable: false);
        if (subKey is null)
        {
            data = null;
            kind = RegistryValueKind.Unknown;
            return false;
        }

        data = subKey.GetValue(valueName);
        if (data is null)
        {
            kind = RegistryValueKind.Unknown;
            return false;
        }

        kind = subKey.GetValueKind(valueName);
        return true;
    }

    public void WriteValue(RegistryHive hive, string path, string valueName, object data, RegistryValueKind kind)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var subKey = baseKey.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"Не удалось открыть/создать раздел реестра: {hive}\\{path}");
        subKey.SetValue(valueName, data, kind);
    }

    public void DeleteValue(RegistryHive hive, string path, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var subKey = baseKey.OpenSubKey(path, writable: true);
        subKey?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteKey(RegistryHive hive, string path)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        baseKey.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }
}
