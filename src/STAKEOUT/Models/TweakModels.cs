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
    public string Hive { get; set; } = "";       // "HKLM" / "HKCU"
    public string SubKey { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public string Kind { get; set; } = "";        // RegistryValueKind name
    public string? ValueBase64 { get; set; }      // original value, encoded
}

/// <summary>Persisted per-tweak state: whether applied, and captured originals.</summary>
public sealed class TweakState
{
    public bool Applied { get; set; }
    public DateTime? AppliedUtc { get; set; }
    public List<SavedValue> Saved { get; set; } = new();
    /// <summary>Free-form notes for command tweaks (e.g. prior powercfg state).</summary>
    public Dictionary<string, string> Notes { get; set; } = new();
}
