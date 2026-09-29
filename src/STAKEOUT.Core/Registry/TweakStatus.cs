namespace Stakeout.Services;

/// <summary>
/// What a tweak's toggle shows, combining what the system reports now with
/// whether STAKEOUT holds the tweak's rollback record.
/// </summary>
/// <param name="IsOn">Toggle position.</param>
/// <param name="CanRevert">
/// Turning the toggle off can restore the previous values: only when STAKEOUT
/// applied the tweak and so captured what was there before.
/// </param>
/// <param name="AppliedOutside">
/// On in the system without a STAKEOUT record: set by Windows, a policy or
/// another tool. It is shown as on, but cannot be undone from here.
/// </param>
public readonly record struct TweakStatus(bool IsOn, bool CanRevert, bool AppliedOutside)
{
    /// <summary>
    /// <paramref name="detected"/>: the system's answer (null when it could not be
    /// read, e.g. a WMI namespace that does not exist); <paramref name="recorded"/>:
    /// STAKEOUT holds a rollback record. The system wins when it answers; the
    /// record is only the fallback.
    /// </summary>
    public static TweakStatus Resolve(bool? detected, bool recorded) => detected switch
    {
        true => new TweakStatus(true, recorded, !recorded),
        // Off in the system even if STAKEOUT once applied it (undone elsewhere).
        false => new TweakStatus(false, recorded, false),
        null => new TweakStatus(recorded, recorded, false),
    };
}
