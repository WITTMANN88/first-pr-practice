using Microsoft.Win32;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>A single reversible system change with metadata for the UI.</summary>
public interface ITweak
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    TweakCategory Category { get; }

    /// <summary>Shows a confirmation dialog before applying (BitLocker, MPO...).</summary>
    bool IsDestructive { get; }
    /// <summary>Change only takes full effect after a reboot (HAGS, power plan...).</summary>
    bool RequiresRestart { get; }
    /// <summary>Requires an explorer.exe restart to be visible (MenuShowDelay...).</summary>
    bool RequiresExplorerRestart { get; }

    bool IsApplied { get; }

    Task<bool> ApplyAsync();
    Task<bool> RevertAsync();
}

/// <summary>A registry write (or key create) with its desired post-apply value.</summary>
public sealed record RegistryOp(
    RegistryHive Hive, string SubKey, string Name, object Value, RegistryValueKind Kind);

/// <summary>
/// Tweak implemented purely as a set of registry writes. On apply it captures
/// each target's prior value into the state store; on revert it restores them
/// exactly (or removes values that did not exist before).
/// </summary>
public sealed class RegistryTweak : ITweak
{
    private readonly TweakStateStore _store;
    private readonly IReadOnlyList<RegistryOp> _ops;

    public RegistryTweak(TweakStateStore store, string id, string title, string description,
        TweakCategory category, IReadOnlyList<RegistryOp> ops,
        bool destructive = false, bool requiresRestart = false, bool requiresExplorerRestart = false)
    {
        _store = store;
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _ops = ops;
        IsDestructive = destructive;
        RequiresRestart = requiresRestart;
        RequiresExplorerRestart = requiresExplorerRestart;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public TweakCategory Category { get; }
    public bool IsDestructive { get; }
    public bool RequiresRestart { get; }
    public bool RequiresExplorerRestart { get; }
    public bool IsApplied => _store.IsApplied(Id);

    public Task<bool> ApplyAsync() => Task.Run(() =>
    {
        var state = _store.GetOrCreate(Id);
        state.Saved.Clear();
        var ok = true;

        foreach (var op in _ops)
        {
            // Capture original BEFORE writing so revert is exact.
            var snap = RegistryHelper.Capture(op.Hive, op.SubKey, op.Name);
            state.Saved.Add(TweakStateStore.ToSaved(op.Hive, op.SubKey, op.Name, snap));

            if (!RegistryHelper.SetValue(op.Hive, op.SubKey, op.Name, op.Value, op.Kind))
                ok = false;
        }

        if (ok)
        {
            _store.MarkApplied(Id, state);
            Logger.Log(Title, "APPLIED");
        }
        else
        {
            Logger.Log(Title, "PARTIAL", "one or more registry writes failed");
        }
        return ok;
    });

    public Task<bool> RevertAsync() => Task.Run(() =>
    {
        var state = _store.GetOrCreate(Id);
        var ok = true;
        // Restore in reverse order for symmetry with apply.
        for (var i = state.Saved.Count - 1; i >= 0; i--)
        {
            if (!TweakStateStore.RestoreSaved(state.Saved[i])) ok = false;
        }
        _store.MarkReverted(Id);
        Logger.Log(Title, ok ? "REVERTED" : "REVERT-PARTIAL");
        return ok;
    });
}

/// <summary>
/// Tweak whose apply/revert are arbitrary async delegates (powercfg, services,
/// WMI, powershell). The delegates are responsible for capturing whatever they
/// need for rollback via the shared <see cref="TweakState"/>.
/// </summary>
public sealed class ActionTweak : ITweak
{
    private readonly TweakStateStore _store;
    private readonly Func<TweakState, Task<bool>> _apply;
    private readonly Func<TweakState, Task<bool>> _revert;

    public ActionTweak(TweakStateStore store, string id, string title, string description,
        TweakCategory category,
        Func<TweakState, Task<bool>> apply, Func<TweakState, Task<bool>> revert,
        bool destructive = false, bool requiresRestart = false, bool requiresExplorerRestart = false)
    {
        _store = store;
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _apply = apply;
        _revert = revert;
        IsDestructive = destructive;
        RequiresRestart = requiresRestart;
        RequiresExplorerRestart = requiresExplorerRestart;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public TweakCategory Category { get; }
    public bool IsDestructive { get; }
    public bool RequiresRestart { get; }
    public bool RequiresExplorerRestart { get; }
    public bool IsApplied => _store.IsApplied(Id);

    public async Task<bool> ApplyAsync()
    {
        var state = _store.GetOrCreate(Id);
        state.Saved.Clear();
        state.Notes.Clear();
        bool ok;
        try
        {
            ok = await _apply(state);
        }
        catch (Exception ex)
        {
            Logger.LogError(Title + " (apply)", ex);
            return false;
        }
        if (ok)
        {
            _store.MarkApplied(Id, state);
            Logger.Log(Title, "APPLIED");
        }
        return ok;
    }

    public async Task<bool> RevertAsync()
    {
        var state = _store.GetOrCreate(Id);
        bool ok;
        try
        {
            ok = await _revert(state);
        }
        catch (Exception ex)
        {
            Logger.LogError(Title + " (revert)", ex);
            return false;
        }
        _store.MarkReverted(Id);
        Logger.Log(Title, ok ? "REVERTED" : "REVERT-PARTIAL");
        return ok;
    }
}
