using System.Globalization;
using Stakeout.Localization;
using Stakeout.Services;

namespace Stakeout.Models;

/// <summary>An installed UWP (Appx) package presented in the removal table.</summary>
public sealed class UwpApp
{
    public string Name { get; set; } = "";            // identity name, e.g. Microsoft.WindowsMaps
    public string DisplayName { get; set; } = "";     // prettified for the UI
    public string PackageFullName { get; set; } = "";
    /// <summary>From the full name ("Name_Version_Arch_ResourceId_PublisherId"), e.g. "x64"; "" if malformed.</summary>
    public string Architecture { get; set; } = "";
    /// <summary>From the full name, e.g. "8000.616.304.0"; "" if malformed.</summary>
    public string Version { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    /// <summary>Absolute path to the package logo, resolved from its manifest (may be null).</summary>
    public string? IconPath { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Protected apps are never auto-selected and cannot be removed here.</summary>
    public bool IsCritical { get; set; }
    /// <summary>Badge category from <see cref="UwpCatalog.Categorize"/>.</summary>
    public UwpCategory Category { get; set; } = UwpCategory.Other;

    public string SizeText => SizeBytes > 0
        ? string.Format(CultureInfo.CurrentCulture, Strings.Unit_Megabytes, SizeBytes / 1024d / 1024d)
        : "—";

    /// <summary>Build an entry from an Appx identity name; category and protection derived from it.</summary>
    public static UwpApp FromIdentity(string name, string packageFullName, string installLocation)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        // Identity names cannot contain '_', so the full name splits cleanly.
        var identity = (packageFullName ?? "").Split('_');
        return new UwpApp
        {
            Version = identity.Length >= 5 ? identity[1] : "",
            Architecture = identity.Length >= 5 ? identity[2] : "",
            Name = name,
            // Drop the publisher prefix ("Microsoft.", "king.com." keeps "com.…" — good enough for display).
            DisplayName = dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name,
            PackageFullName = packageFullName ?? "",
            InstallLocation = installLocation,
            IsCritical = UwpCatalog.IsCritical(name),
            Category = UwpCatalog.Categorize(name),
        };
    }
}
