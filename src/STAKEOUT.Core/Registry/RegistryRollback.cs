using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Capture-then-write / restore engine behind every reversible tweak.
///
/// Rules:
///   * The prior value is captured into <see cref="TweakState.Saved"/> BEFORE a
///     write, so the tweak can be reverted exactly — including deleting values
///     that did not exist before.
///   * <see cref="ApplyAll"/> is transactional: if any write fails, everything
///     already written in that call is restored, so a failed tweak never leaves
///     half-applied changes without a rollback record.
///   * <see cref="RestoreAll"/> walks the captures in reverse order, so if a
///     value was written twice the ORIGINAL (first capture) wins.
/// </summary>
public sealed class RegistryRollback
{
    private readonly IRegistryAccess _registry;

    public RegistryRollback(IRegistryAccess registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public IRegistryAccess Registry => _registry;

    /// <summary>Capture the current value into <paramref name="state"/>, then write.</summary>
    public bool CaptureAndSet(TweakState state, RegHive hive, string subKey, string name,
        object value, RegValueKind kind)
    {
        var snap = _registry.Capture(hive, subKey, name);
        state.Saved.Add(SavedValueCodec.ToSaved(hive, subKey, name, snap));
        return _registry.SetValue(hive, subKey, name, value, kind);
    }

    /// <summary>
    /// Write only if the value already exists (used when walking device trees, to
    /// avoid creating values that were never there). Returns true if written.
    /// </summary>
    public bool SetIfExists(TweakState state, RegHive hive, string subKey, string name,
        object value, RegValueKind kind)
    {
        var snap = _registry.Capture(hive, subKey, name);
        if (!snap.Existed) return false;
        state.Saved.Add(SavedValueCodec.ToSaved(hive, subKey, name, snap));
        return _registry.SetValue(hive, subKey, name, value, kind);
    }

    /// <summary>
    /// Apply every op or none: on the first failed write, restore what this call
    /// already changed and return false (the captures are removed from the state).
    /// </summary>
    public bool ApplyAll(TweakState state, IReadOnlyList<RegistryOp> ops)
    {
        var start = state.Saved.Count;
        foreach (var op in ops)
        {
            if (CaptureAndSet(state, op.Hive, op.SubKey, op.Name, op.Value, op.Kind)) continue;

            Logger.Log("Rollback", "PARTIAL", $@"write failed at {op.SubKey}\{op.Name}; undoing this apply");
            RestoreRange(state, start);
            state.Saved.RemoveRange(start, state.Saved.Count - start);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Restore every captured value in reverse order. Continues past failures and
    /// returns false if any restore failed or a record could not be decoded.
    /// </summary>
    public bool RestoreAll(TweakState state) => RestoreRange(state, 0);

    private bool RestoreRange(TweakState state, int start)
    {
        var ok = true;
        for (var i = state.Saved.Count - 1; i >= start; i--)
        {
            if (!SavedValueCodec.TryDecode(state.Saved[i], out var d))
            {
                Logger.Log("Rollback", "ERROR", $"undecodable saved value #{i} ({state.Saved[i].SubKey}\\{state.Saved[i].Name})");
                ok = false;
                continue;
            }

            var v = d.Value;
            var restored = v.Existed
                ? _registry.SetValue(v.Hive, v.SubKey, v.Name, v.Value!, v.Kind)
                : _registry.DeleteValue(v.Hive, v.SubKey, v.Name);
            if (!restored) ok = false;
        }
        return ok;
    }
}
