using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>A single reversible system change with metadata for the UI.</summary>
public interface ITweak
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    /// <summary>Plain-language explanation for the [?] tooltip: what it does, pros and cons.</summary>
    string ExtendedDescription { get; }
    /// <summary>What exactly changes (registry values, commands), shown in the help tooltip.</summary>
    string Details { get; }
    TweakCategory Category { get; }

    /// <summary>Shows a confirmation dialog before applying (BitLocker, MPO...).</summary>
    bool IsDestructive { get; }
    /// <summary>Change only takes full effect after a reboot (HAGS, UAC...).</summary>
    bool RequiresRestart { get; }
    /// <summary>Requires an explorer.exe restart to be visible (MenuShowDelay...).</summary>
    bool RequiresExplorerRestart { get; }

    /// <summary>
    /// STAKEOUT applied this tweak and holds its rollback record (the values it
    /// replaced), so it can be reverted. Says nothing about the system itself:
    /// see <see cref="ReadCurrentState"/>.
    /// </summary>
    bool HasRollbackRecord { get; }

    /// <summary>
    /// Whether the system has this tweak's settings right now, whoever set them:
    /// true / false, or null when it cannot be told (fall back to the record).
    /// A missing key or value reads as false. Read-only, never throws, does
    /// no logging on the normal path; may do registry / WMI I/O, so call it off
    /// the UI thread.
    /// </summary>
    bool? ReadCurrentState();

    Task<bool> ApplyAsync();
    Task<bool> RevertAsync();
}

/// <summary>
/// Runs a tweak's state probe: an exception means "unknown" (null), and is
/// logged once per tweak per session, so a scan repeated on every visit to the
/// page cannot flood the log.
/// </summary>
internal static class TweakProbe
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Reported = new(StringComparer.Ordinal);

    public static bool? Run(string tweakId, Func<bool?> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception ex)
        {
            if (Reported.TryAdd(tweakId, true)) Logger.LogError("Tweaks.Scan " + tweakId, ex);
            return null;
        }
    }
}

/// <summary>
/// Display metadata shared by the tweak implementations. <paramref name="Details"/>
/// is required for action tweaks; a registry tweak derives it from its writes.
/// </summary>
public sealed record TweakInfo(
    string Id, string Title, string Description, string ExtendedDescription, TweakCategory Category,
    bool Destructive = false, bool RequiresRestart = false, bool RequiresExplorerRestart = false,
    string? Details = null);

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
        Details = info.Details ?? TweakDetails.Registry(ops);
    }

    public string Id => _info.Id;
    public string Title => _info.Title;
    public string Description => _info.Description;
    public string ExtendedDescription => _info.ExtendedDescription;
    public string Details { get; }
    public TweakCategory Category => _info.Category;
    public bool IsDestructive => _info.Destructive;
    public bool RequiresRestart => _info.RequiresRestart;
    public bool RequiresExplorerRestart => _info.RequiresExplorerRestart;
    public bool HasRollbackRecord => _store.IsApplied(Id);

    /// <summary>On when every value this tweak writes is in place.</summary>
    public bool? ReadCurrentState() => TweakProbe.Run(Id, () => _rollback.AllMatch(_ops));

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
    private readonly Func<bool?> _detect;

    /// <param name="store">Rollback records.</param>
    /// <param name="rollback">Registry capture / restore engine.</param>
    /// <param name="info">Display metadata.</param>
    /// <param name="apply">Applies the tweak, capturing what it replaces into the state.</param>
    /// <param name="revert">Restores from the state.</param>
    /// <param name="detect">Reads whether the system has the tweak's settings (see <see cref="ITweak.ReadCurrentState"/>).</param>
    public ActionTweak(TweakStateStore store, RegistryRollback rollback, TweakInfo info,
        Func<TweakState, Task<bool>> apply, Func<TweakState, Task<bool>> revert, Func<bool?> detect)
    {
        _store = store;
        _rollback = rollback;
        _info = info;
        _apply = apply;
        _revert = revert;
        _detect = detect;
    }

    public string Id => _info.Id;
    public string Title => _info.Title;
    public string Description => _info.Description;
    public string ExtendedDescription => _info.ExtendedDescription;
    public string Details => _info.Details ?? "";
    public TweakCategory Category => _info.Category;
    public bool IsDestructive => _info.Destructive;
    public bool RequiresRestart => _info.RequiresRestart;
    public bool RequiresExplorerRestart => _info.RequiresExplorerRestart;
    public bool HasRollbackRecord => _store.IsApplied(Id);

    public bool? ReadCurrentState() => TweakProbe.Run(Id, _detect);

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
