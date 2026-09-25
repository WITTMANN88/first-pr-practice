using Microsoft.Win32;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Helpers shared by <see cref="ActionTweak"/> delegates so command-style tweaks
/// get the same exact-rollback behaviour as <see cref="RegistryTweak"/>: capture
/// the prior value into the tweak's state before writing, and restore everything
/// on revert.
/// </summary>
public static class TweakOps
{
    /// <summary>Capture the current value into state, then write the new one.</summary>
    public static bool CaptureAndSet(TweakState state, RegistryHive hive, string subKey,
        string name, object value, RegistryValueKind kind)
    {
        var snap = RegistryHelper.Capture(hive, subKey, name);
        state.Saved.Add(TweakStateStore.ToSaved(hive, subKey, name, snap));
        return RegistryHelper.SetValue(hive, subKey, name, value, kind);
    }

    /// <summary>Restore every captured value (reverse order). Used by revert.</summary>
    public static Task<bool> RestoreAll(TweakState state) => Task.Run(() =>
    {
        var ok = true;
        for (var i = state.Saved.Count - 1; i >= 0; i--)
            if (!TweakStateStore.RestoreSaved(state.Saved[i])) ok = false;
        return ok;
    });
}
