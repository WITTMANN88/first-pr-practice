using Microsoft.Win32;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>
/// Thin, exception-safe wrapper over the Windows registry.
///
/// Every method targets the 64-bit view explicitly (Registry64) so that on a
/// 64-bit OS we never get silently redirected into WOW6432Node. All operations
/// swallow nothing silently — failures are logged and surfaced via the boolean
/// return so callers can react.
/// </summary>
public static class RegistryHelper
{
    /// <summary>A captured registry value plus whether it existed at capture time.</summary>
    public readonly record struct ValueSnapshot(object? Value, RegistryValueKind Kind, bool Existed);

    private static RegistryKey BaseKey(RegistryHive hive)
        => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    /// <summary>
    /// Log a registry failure, distinguishing permission problems so
    /// "Access Denied" is always visible in the encrypted TEMP log with an
    /// explicit ACCESS_DENIED status.
    /// </summary>
    private static void LogRegistryError(string action, Exception ex)
    {
        if (ex is UnauthorizedAccessException or System.Security.SecurityException)
            Logger.Log(action, "ACCESS_DENIED", ex.Message);
        else
            Logger.LogError(action, ex);
    }

    /// <summary>Read a value. Returns snapshot with Existed=false when missing.</summary>
    public static ValueSnapshot Capture(RegistryHive hive, string subKey, string name)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key == null) return new ValueSnapshot(null, RegistryValueKind.Unknown, false);

            var value = key.GetValue(name, null);
            if (value == null) return new ValueSnapshot(null, RegistryValueKind.Unknown, false);

            var kind = key.GetValueKind(name);
            return new ValueSnapshot(value, kind, true);
        }
        catch (Exception ex)
        {
            LogRegistryError($"Registry.Capture {subKey}\\{name}", ex);
            return new ValueSnapshot(null, RegistryValueKind.Unknown, false);
        }
    }

    /// <summary>Create the key path if needed and write a value.</summary>
    public static bool SetValue(RegistryHive hive, string subKey, string name,
        object value, RegistryValueKind kind)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.CreateSubKey(subKey, writable: true);
            if (key == null) return false;
            key.SetValue(name, value, kind);
            return true;
        }
        catch (Exception ex)
        {
            LogRegistryError($"Registry.SetValue {subKey}\\{name}", ex);
            return false;
        }
    }

    /// <summary>Delete a single value. Missing value is treated as success.</summary>
    public static bool DeleteValue(RegistryHive hive, string subKey, string name)
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

    /// <summary>Restore a value to a previously captured snapshot (or delete it).</summary>
    public static bool Restore(RegistryHive hive, string subKey, string name, ValueSnapshot snap)
    {
        return snap.Existed && snap.Value != null
            ? SetValue(hive, subKey, name, snap.Value, snap.Kind)
            : DeleteValue(hive, subKey, name);
    }

    /// <summary>Read a value as string, or return <paramref name="fallback"/>.</summary>
    public static string? ReadString(RegistryHive hive, string subKey, string name, string? fallback = null)
    {
        var snap = Capture(hive, subKey, name);
        return snap.Existed ? snap.Value?.ToString() : fallback;
    }

    /// <summary>Enumerate value names directly under a key (empty on error).</summary>
    public static IReadOnlyList<string> ValueNames(RegistryHive hive, string subKey)
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
    public static IReadOnlyList<string> SubKeyNames(RegistryHive hive, string subKey)
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
