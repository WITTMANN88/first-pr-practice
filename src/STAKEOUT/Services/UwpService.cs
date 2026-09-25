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
