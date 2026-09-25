using System.IO;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Lists and removes UWP (Appx) packages via PowerShell (Get-AppxPackage /
/// Remove-AppxPackage) for all user accounts.
///
/// A guard list protects packages that are unsafe to remove — the Store and its
/// purchase app, the App Installer (winget!), the Calculator, and the shared
/// framework libraries (VCLibs, .NET Native, UI.Xaml) that other apps depend on.
/// Protected packages are never auto-selected and are rejected by the remover.
/// </summary>
public sealed class UwpService
{
    /// <summary>Case-insensitive substrings that mark a package as protected.</summary>
    private static readonly string[] CriticalMarkers =
    {
        "WindowsStore",            // Microsoft Store
        "StorePurchaseApp",
        "DesktopAppInstaller",     // App Installer / winget
        "WindowsCalculator",       // Калькулятор
        "VCLibs",                  // C++ runtime
        "NET.Native.Framework",
        "NET.Native.Runtime",
        "UI.Xaml",                 // WinUI runtime
        "Microsoft.Services.Store",
        "StoreExperienceHost",
        "SecHealthUI",             // Windows Security UI
        "ShellExperienceHost",
        "Windows.StartMenuExperienceHost",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl",
    };

    /// <summary>Enumerate all installed packages for all users.</summary>
    public async Task<List<UwpApp>> ListAsync()
    {
        // Emit a stable pipe-delimited line per package.
        const string script =
            "Get-AppxPackage -AllUsers | ForEach-Object { " +
            "\"$($_.Name)|$($_.PackageFullName)|$($_.InstallLocation)\" }";

        var result = await PowerShellRunner.RunScriptAsync(script);
        var apps = new List<UwpApp>();
        if (!result.Success && string.IsNullOrWhiteSpace(result.StdOut))
        {
            Logger.Log("UWP list", "ERROR", result.StdErr);
            return apps;
        }

        foreach (var raw in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split('|');
            if (parts.Length < 2) continue;

            var name = parts[0].Trim();
            var full = parts[1].Trim();
            var loc = parts.Length > 2 ? parts[2].Trim() : "";

            apps.Add(new UwpApp
            {
                Name = name,
                DisplayName = Prettify(name),
                PackageFullName = full,
                InstallLocation = loc,
                IconPath = ResolveIcon(loc),
                IsCritical = IsCritical(name),
                SizeBytes = 0, // computed lazily right before removal (fast listing)
            });
        }

        // De-duplicate by package full name and sort junk first.
        var deduped = apps
            .GroupBy(a => a.PackageFullName)
            .Select(g => g.First())
            .OrderBy(a => a.IsCritical)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Logger.Log("UWP list", "OK", $"{deduped.Count} package(s)");
        return deduped;
    }

    /// <summary>
    /// Remove one package for all users. Refuses protected packages. Returns the
    /// number of bytes freed (measured before removal) on success, else 0.
    /// </summary>
    public async Task<long> RemoveAsync(UwpApp app)
    {
        if (app.IsCritical)
        {
            Logger.Log("UWP remove", "BLOCKED", $"{app.Name} is protected");
            return 0;
        }

        var freed = MeasureSize(app.InstallLocation);

        // Remove strictly by package full name to avoid wildcard mishaps.
        var script =
            $"Remove-AppxPackage -Package '{Escape(app.PackageFullName)}' -AllUsers -ErrorAction Stop";
        var result = await PowerShellRunner.RunScriptAsync(script);

        if (result.Success)
        {
            Logger.Log("UWP remove", "OK", $"{app.Name} (~{freed / 1024 / 1024} MB)");
            return freed;
        }

        Logger.Log("UWP remove", "ERROR", $"{app.Name}: {result.StdErr.Trim()}");
        return 0;
    }

    private bool IsCritical(string name)
        => CriticalMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Best-effort resolution of a package's logo for the table thumbnail.
    /// Reads AppxManifest.xml, finds the square logo reference, and picks a real
    /// file on disk (logos ship with scale/targetsize qualifiers, e.g.
    /// "Square44x44Logo.scale-200.png"). Returns null when nothing usable is found.
    /// Fully guarded: a locked folder or malformed manifest never throws.
    /// </summary>
    private static string? ResolveIcon(string installLocation)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
                return null;

            var manifest = Path.Combine(installLocation, "AppxManifest.xml");
            if (!File.Exists(manifest)) return null;

            var doc = System.Xml.Linq.XDocument.Load(manifest);
            // Search by local name to be namespace-agnostic across manifest versions.
            var logoRel =
                doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value
                ?? doc.Descendants()
                      .Where(e => e.Name.LocalName == "VisualElements")
                      .Select(e => (string?)e.Attribute("Square44x44Logo") ?? (string?)e.Attribute("Square150x150Logo"))
                      .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (string.IsNullOrWhiteSpace(logoRel)) return null;

            // Normalize separators and split into folder + base filename.
            var rel = logoRel.Replace('/', '\\');
            var dir = Path.Combine(installLocation, Path.GetDirectoryName(rel) ?? "");
            var baseName = Path.GetFileNameWithoutExtension(rel);
            var ext = Path.GetExtension(rel);
            if (!Directory.Exists(dir)) return null;

            // Exact file first, then any scale/targetsize variant.
            var exact = Path.Combine(dir, baseName + ext);
            if (File.Exists(exact)) return exact;

            var candidates = Directory.EnumerateFiles(dir, baseName + "*" + ext).ToList();
            // Prefer a mid-size asset if several exist.
            return candidates.FirstOrDefault(f => f.Contains("targetsize-32", StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(f => f.Contains("scale-200", StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault();
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Log("UWP icon", "ACCESS_DENIED", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError("UWP icon", ex);
            return null;
        }
    }

    /// <summary>Sum the install folder size (best-effort, guarded).</summary>
    private static long MeasureSize(string location)
    {
        if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location)) return 0;
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(location, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { /* skip locked file */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static string Prettify(string identityName)
    {
        // Drop the publisher prefix ("Microsoft.") and split CamelCase lightly.
        var n = identityName;
        var dot = n.IndexOf('.');
        if (dot >= 0 && dot < n.Length - 1) n = n[(dot + 1)..];
        return n;
    }

    private static string Escape(string s) => s.Replace("'", "''");
}
