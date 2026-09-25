namespace Stakeout.Models;

/// <summary>An installed UWP (Appx) package presented in the removal table.</summary>
public sealed class UwpApp
{
    public string Name { get; set; } = "";            // identity name, e.g. Microsoft.WindowsMaps
    public string DisplayName { get; set; } = "";     // prettified for the UI
    public string PackageFullName { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    /// <summary>Absolute path to the package logo, resolved from its manifest (may be null).</summary>
    public string? IconPath { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Protected apps are never auto-selected and cannot be removed here.</summary>
    public bool IsCritical { get; set; }

    public string SizeText => SizeBytes > 0
        ? $"{SizeBytes / 1024d / 1024d:0.#} МБ"
        : "—";
}
