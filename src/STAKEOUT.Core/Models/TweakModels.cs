namespace Stakeout.Models;

/// <summary>Logical grouping shown as a section header on the Tweaks page.</summary>
public enum TweakCategory
{
    Privacy,      // Телеметрия, Advertising ID, Cortana, DiagTrack
    Security,     // UAC, BitLocker
    Performance,  // Power throttling, MPO, HAGS, power plan, network, USB
    Gaming,       // GameDVR/Xbox, Game Mode, mouse
    System,       // Hibernation, driver updates, animations
    Interface     // MenuShowDelay, animations, transparency
}

/// <summary>
/// One persisted registry value captured before a tweak was applied, so the
/// tweak can be reverted to the exact prior state (or deleted if it was absent).
/// Stored as strings for portable JSON serialization.
/// </summary>
public sealed class SavedValue
{
    public string Hive { get; set; } = "";       // "HKLM" / "HKCU" / "HKU" / "HKCR" / "HKCC"
    public string SubKey { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public string Kind { get; set; } = "";        // RegistryValueKind name
    public string? ValueBase64 { get; set; }      // original value, encoded (RegistryValueCodec)

    public SavedValue Clone() => (SavedValue)MemberwiseClone(); // all members immutable
}

/// <summary>
/// Per-tweak rollback record: whether applied, and the originals captured before
/// the tweak wrote anything. A tweak builds its own instance while applying; the
/// store only ever keeps deep copies, so no instance is shared across threads.
/// </summary>
public sealed class TweakState
{
    public bool Applied { get; set; }
    public DateTime? AppliedUtc { get; set; }
    // Collections are init-only: callers add to them, never swap them out.
    public IList<SavedValue> Saved { get; init; } = new List<SavedValue>();
    /// <summary>Free-form notes for command tweaks (e.g. prior powercfg scheme).</summary>
    public IDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();

    public TweakState Clone() => new()
    {
        Applied = Applied,
        AppliedUtc = AppliedUtc,
        Saved = Saved.Select(s => s.Clone()).ToList(),
        Notes = new Dictionary<string, string>(Notes),
    };
}
