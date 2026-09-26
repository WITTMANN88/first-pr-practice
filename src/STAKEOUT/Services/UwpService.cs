using System.IO;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>Outcome of removing one package.</summary>
public readonly record struct UwpRemovalResult(bool Success, long FreedBytes);

/// <summary>UWP package listing and removal (the view model's dependency; faked at design time).</summary>
public interface IUwpService
{
    /// <summary>Enumerate all installed packages for all users.</summary>
    Task<IReadOnlyList<UwpApp>> ListAsync();

    /// <summary>Remove one package for all users; protected packages are refused.</summary>
    Task<UwpRemovalResult> RemoveAsync(UwpApp app);
}

/// <summary>
/// Lists and removes UWP (Appx) packages via PowerShell (Get-AppxPackage /
/// Remove-AppxPackage) for all user accounts. Parsing, categorisation and the
/// protection list live in the core (<see cref="UwpListing"/>, <see cref="UwpCatalog"/>);
/// this class only runs the scripts and touches the file system.
/// Protected packages are never auto-selected and are rejected by the remover.
/// </summary>
public sealed class UwpService : IUwpService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<UwpApp>> ListAsync()
    {
        // Emit a stable pipe-delimited line per package.
        const string script =
            "Get-AppxPackage -AllUsers | ForEach-Object { " +
            "\"$($_.Name)|$($_.PackageFullName)|$($_.InstallLocation)\" }";

        var result = await PowerShellRunner.RunScriptAsync(script);
        if (!result.Success && string.IsNullOrWhiteSpace(result.StdOut))
        {
            Logger.Log("UWP list", "ERROR", result.StdErr);
            return Array.Empty<UwpApp>();
        }

        // Parsing ~150 manifests and probing icon files is disk work: keep it off
        // the UI thread (this method is awaited from the view model).
        var apps = await Task.Run(() =>
        {
            var parsed = UwpListing.Parse(result.StdOut);
            foreach (var app in parsed) app.IconPath = ResolveIcon(app.InstallLocation);
            return parsed;
        });

        Logger.Log("UWP list", "OK", $"{apps.Count} package(s)");
        return apps;
    }

    /// <summary>
    /// Remove one package for all users. Refuses protected packages. On success
    /// reports the bytes freed (measured before removal).
    /// </summary>
    public async Task<UwpRemovalResult> RemoveAsync(UwpApp app)
    {
        if (app.IsCritical)
        {
            Logger.Log("UWP remove", "BLOCKED", $"{app.Name} is protected");
            return new UwpRemovalResult(false, 0);
        }

        // Walks every file of the package: off the UI thread.
        var freed = await Task.Run(() => MeasureSize(app.InstallLocation));

        // Remove strictly by package full name to avoid wildcard mishaps.
        var script =
            $"Remove-AppxPackage -Package '{Escape(app.PackageFullName)}' -AllUsers -ErrorAction Stop";
        var result = await PowerShellRunner.RunScriptAsync(script);

        if (result.Success)
        {
            Logger.Log("UWP remove", "OK", $"{app.Name} (~{freed / 1024 / 1024} MB)");
            return new UwpRemovalResult(true, freed);
        }

        Logger.Log("UWP remove", "ERROR", $"{app.Name}: {result.StdErr.Trim()}");
        return new UwpRemovalResult(false, 0);
    }

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

    /// <summary>Escape for a single-quoted PowerShell string literal.</summary>
    private static string Escape(string s) => s.Replace("'", "''", StringComparison.Ordinal);
}
