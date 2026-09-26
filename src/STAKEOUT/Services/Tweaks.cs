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
    /// <summary>Change only takes full effect after a reboot (HAGS, UAC...).</summary>
    bool RequiresRestart { get; }
    /// <summary>Requires an explorer.exe restart to be visible (MenuShowDelay...).</summary>
    bool RequiresExplorerRestart { get; }

    bool IsApplied { get; }

    Task<bool> ApplyAsync();
    Task<bool> RevertAsync();
}

/// <summary>Display metadata shared by the tweak implementations.</summary>
public sealed record TweakInfo(
    string Id, string Title, string Description, TweakCategory Category,
    bool Destructive = false, bool RequiresRestart = false, bool RequiresExplorerRestart = false);

/// <summary>
/// Tweak implemented purely as registry writes, applied transactionally through
/// <see cref="RegistryRollback"/>: originals are captured first, a failed write
/// undoes the whole apply, and revert restores exactly what was there before.
/// </summary>
public sealed class RegistryTweak : ITweak
{
    private readonly TweakStateStore _store;
    private readonly RegistryRollback _rollback;
    private readonly TweakInfo _info;
    private readonly IReadOnlyList<RegistryOp> _ops;

    public RegistryTweak(TweakStateStore store, RegistryRollback rollback, TweakInfo info,
        IReadOnlyList<RegistryOp> ops)
    {
        _store = store;
        _rollback = rollback;
        _info = info;
        _ops = ops;
    }

    public string Id => _info.Id;
    public string Title => _info.Title;
    public string Description => _info.Description;
    public TweakCategory Category => _info.Category;
    public bool IsDestructive => _info.Destructive;
    public bool RequiresRestart => _info.RequiresRestart;
    public bool RequiresExplorerRestart => _info.RequiresExplorerRestart;
    public bool IsApplied => _store.IsApplied(Id);

    public Task<bool> ApplyAsync() => Task.Run(() =>
    {
        var state = new TweakState(); // private to this call until handed to the store
        if (!_rollback.ApplyAll(state, _ops))
        {
            Logger.Log(Id, "FAILED", "apply rolled back");
            return false;
        }
        if (!_store.MarkApplied(Id, state))
        {
            // Changes are in the registry but the rollback record could not be
            // persisted: undo rather than leave an unrecoverable change behind.
            _rollback.RestoreAll(state);
            Logger.Log(Id, "FAILED", "state not persisted; changes undone");
            return false;
        }
        Logger.Log(Id, "APPLIED");
        return true;
    });

    public Task<bool> RevertAsync() => Task.Run(() =>
    {
        var state = _store.GetApplied(Id);
        if (state == null) return true; // nothing recorded → nothing to undo

        if (!_rollback.RestoreAll(state))
        {
            // Keep the record so the user can retry the revert.
            Logger.Log(Id, "REVERT-PARTIAL", "record kept for retry");
            return false;
        }
        _store.MarkReverted(Id);
        Logger.Log(Id, "REVERTED");
        return true;
    });
}

/// <summary>
/// Tweak whose apply/revert are arbitrary async delegates (powercfg, services,
/// WMI). Delegates capture what they need for rollback into the given
/// <see cref="TweakState"/> (registry writes via <see cref="RegistryRollback"/>).
/// If apply fails, any registry values it already captured are restored.
/// </summary>
public sealed class ActionTweak : ITweak
{
    private readonly TweakStateStore _store;
    private readonly RegistryRollback _rollback;
    private readonly TweakInfo _info;
    private readonly Func<TweakState, Task<bool>> _apply;
    private readonly Func<TweakState, Task<bool>> _revert;

    public ActionTweak(TweakStateStore store, RegistryRollback rollback, TweakInfo info,
        Func<TweakState, Task<bool>> apply, Func<TweakState, Task<bool>> revert)
    {
        _store = store;
        _rollback = rollback;
        _info = info;
        _apply = apply;
        _revert = revert;
    }

    public string Id => _info.Id;
    public string Title => _info.Title;
    public string Description => _info.Description;
    public TweakCategory Category => _info.Category;
    public bool IsDestructive => _info.Destructive;
    public bool RequiresRestart => _info.RequiresRestart;
    public bool RequiresExplorerRestart => _info.RequiresExplorerRestart;
    public bool IsApplied => _store.IsApplied(Id);

    public async Task<bool> ApplyAsync()
    {
        var state = new TweakState();
        bool ok;
        try
        {
            ok = await _apply(state);
        }
        catch (Exception ex)
        {
            Logger.LogError(Id + " (apply)", ex);
            ok = false;
        }

        if (ok && _store.MarkApplied(Id, state))
        {
            Logger.Log(Id, "APPLIED");
            return true;
        }

        // Failed (or not persisted): undo registry writes the delegate made.
        if (state.Saved.Count > 0) _rollback.RestoreAll(state);
        Logger.Log(Id, "FAILED", "apply rolled back");
        return false;
    }

    public async Task<bool> RevertAsync()
    {
        var state = _store.GetApplied(Id);
        if (state == null) return true;

        bool ok;
        try
        {
            ok = await _revert(state);
        }
        catch (Exception ex)
        {
            Logger.LogError(Id + " (revert)", ex);
            ok = false;
        }

        if (!ok)
        {
            Logger.Log(Id, "REVERT-PARTIAL", "record kept for retry");
            return false;
        }
        _store.MarkReverted(Id);
        Logger.Log(Id, "REVERTED");
        return true;
    }
}
