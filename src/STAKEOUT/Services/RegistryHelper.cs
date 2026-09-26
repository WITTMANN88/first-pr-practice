using Microsoft.Win32;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>
/// Thin, exception-safe wrapper over the Windows registry.
///
/// Public API uses the core's <see cref="RegHive"/> / <see cref="RegValueKind"/>;
/// conversion to the Win32 enums is a plain cast (identical values, pinned by a
/// unit test). Every method targets the 64-bit view explicitly (Registry64) so a
/// 64-bit OS never silently redirects into WOW6432Node. Nothing throws: failures
/// are logged (permission problems as ACCESS_DENIED) and surfaced via returns.
/// </summary>
public static class RegistryHelper
{
    private static RegistryKey BaseKey(RegHive hive)
        => RegistryKey.OpenBaseKey((RegistryHive)hive, RegistryView.Registry64);

    private static void LogRegistryError(string action, Exception ex)
    {
        if (ex is UnauthorizedAccessException or System.Security.SecurityException)
            Logger.Log(action, "ACCESS_DENIED", ex.Message);
        else
            Logger.LogError(action, ex);
    }

    /// <summary>Read a value. Returns <see cref="RegistryValueSnapshot.Absent"/> when missing.</summary>
    public static RegistryValueSnapshot Capture(RegHive hive, string subKey, string name)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key == null) return RegistryValueSnapshot.Absent;

            // DoNotExpandEnvironmentNames: capture REG_EXPAND_SZ verbatim, so a
            // rollback writes back "%SystemRoot%\..." and not the expanded path.
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null) return RegistryValueSnapshot.Absent;

            return new RegistryValueSnapshot(value, (RegValueKind)key.GetValueKind(name), true);
        }
        catch (Exception ex)
        {
            LogRegistryError($"Registry.Capture {subKey}\\{name}", ex);
            return RegistryValueSnapshot.Absent;
        }
    }

    /// <summary>Create the key path if needed and write a value.</summary>
    public static bool SetValue(RegHive hive, string subKey, string name, object value, RegValueKind kind)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.CreateSubKey(subKey, writable: true);
            if (key == null) return false;
            key.SetValue(name, value, (RegistryValueKind)kind);
            return true;
        }
        catch (Exception ex)
        {
            LogRegistryError($"Registry.SetValue {subKey}\\{name}", ex);
            return false;
        }
    }

    /// <summary>Delete a single value. Missing value is treated as success.</summary>
    public static bool DeleteValue(RegHive hive, string subKey, string name)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.OpenSubKey(subKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            LogRegistryError($"Registry.DeleteValue {subKey}\\{name}", ex);
            return false;
        }
    }

    /// <summary>Read a value as string, or return <paramref name="fallback"/>.</summary>
    public static string? ReadString(RegHive hive, string subKey, string name, string? fallback = null)
    {
        var snap = Capture(hive, subKey, name);
        return snap.Existed ? snap.Value?.ToString() : fallback;
    }

    /// <summary>Enumerate value names directly under a key (empty on error).</summary>
    public static IReadOnlyList<string> ValueNames(RegHive hive, string subKey)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            return key?.GetValueNames() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            LogRegistryError($"Registry.ValueNames {subKey}", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>Enumerate immediate sub-key names under a path (empty on error).</summary>
    public static IReadOnlyList<string> SubKeyNames(RegHive hive, string subKey)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            LogRegistryError($"Registry.SubKeyNames {subKey}", ex);
            return Array.Empty<string>();
        }
    }
}

/// <summary>The real Windows registry behind the core's <see cref="IRegistryAccess"/>.</summary>
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    public RegistryValueSnapshot Capture(RegHive hive, string subKey, string name)
        => RegistryHelper.Capture(hive, subKey, name);

    public bool SetValue(RegHive hive, string subKey, string name, object value, RegValueKind kind)
        => RegistryHelper.SetValue(hive, subKey, name, value, kind);

    public bool DeleteValue(RegHive hive, string subKey, string name)
        => RegistryHelper.DeleteValue(hive, subKey, name);
}
