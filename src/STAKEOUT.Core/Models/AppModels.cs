using System.Globalization;
using Stakeout.Localization;
using Stakeout.Services;

namespace Stakeout.Models;

/// <summary>How an app is installed, and so how it is removed.</summary>
public enum AppKind
{
    /// <summary>Store / Appx package: Remove-AppxPackage.</summary>
    Uwp,
    /// <summary>Classic Win32 program with a registry Uninstall entry: its own uninstaller.</summary>
    Desktop,
}

/// <summary>Registry branch a desktop program's Uninstall entry was found in.</summary>
public enum UninstallSource
{
    /// <summary>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall (64-bit programs).</summary>
    LocalMachine,
    /// <summary>HKLM\SOFTWARE\WOW6432Node\…\Uninstall (32-bit programs on 64-bit Windows).</summary>
    LocalMachine32,
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall (installed for this user only).</summary>
    CurrentUser,
}

/// <summary>An installed program in the Apps table: a UWP package or a desktop (Win32) program.</summary>
public sealed class InstalledApp
{
    public AppKind Kind { get; set; } = AppKind.Uwp;
    public string Name { get; set; } = "";            // UWP identity name (Microsoft.WindowsMaps) or Uninstall key name
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
    /// <summary>Badge category from <see cref="UwpCatalog.Categorize"/>; <see cref="AppCategory.Desktop"/> for Win32.</summary>
    public AppCategory Category { get; set; } = AppCategory.Other;

    // --- desktop (Win32) programs only ---------------------------------------

    public string Publisher { get; set; } = "";
    /// <summary>Icon index inside <see cref="IconPath"/> when it is an .exe/.dll (DisplayIcon "file,index").</summary>
    public int IconIndex { get; set; }
    /// <summary>UninstallString (or QuietUninstallString when there is no other), as stored.</summary>
    public string UninstallCommand { get; set; } = "";
    public UninstallSource Source { get; set; }
    /// <summary>Installed for the current user only (HKCU): its uninstaller must not run elevated.</summary>
    public bool IsPerUser => Kind == AppKind.Desktop && Source == UninstallSource.CurrentUser;

    public string SizeText => SizeBytes > 0
        ? string.Format(CultureInfo.CurrentCulture, Strings.Unit_Megabytes, SizeBytes / 1024d / 1024d)
        : "—";

    /// <summary>Build an entry from an Appx identity name; category and protection derived from it.</summary>
    public static InstalledApp FromIdentity(string name, string packageFullName, string installLocation)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        // Identity names cannot contain '_', so the full name splits cleanly.
        var identity = (packageFullName ?? "").Split('_');
        return new InstalledApp
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
