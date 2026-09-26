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

    /// <summary>Size of the package's install folder in bytes (0 when unknown).</summary>
    Task<long> MeasureSizeAsync(UwpApp app);
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

        // Reuse the size measured for the table; otherwise walk the files now.
        var freed = app.SizeBytes > 0 ? app.SizeBytes : await MeasureSizeAsync(app);

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
    /// Reads AppxManifest.xml and tries, in order, the app-list icon
    /// (Square44x44Logo, what Start shows), the tile (Square150x150Logo) and the
    /// Store logo (Properties/Logo). System packages often ship only a template
    /// placeholder as their Store logo, so it comes last. Logos ship with
    /// scale/targetsize qualifiers ("Square44x44Logo.targetsize-32_altform-unplated.png");
    /// the first reference that resolves to a real file wins. Returns null when
    /// nothing usable is found. Fully guarded: a locked folder or malformed
    /// manifest never throws.
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
            var visual = doc.Descendants().Where(e => e.Name.LocalName == "VisualElements").ToList();
            var references = visual.Select(e => (string?)e.Attribute("Square44x44Logo"))
                .Concat(visual.Select(e => (string?)e.Attribute("Square150x150Logo")))
                .Append(doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value);

            foreach (var reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference)) continue;
                var file = ResolveLogoFile(installLocation, reference);
                if (file != null) return file;
            }
            return null;
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

    /// <summary>Qualifier preference: 32 px on a dark row (unplated), then 32 px, then 200 % scale.</summary>
    private static readonly string[] PreferredQualifiers =
        { "targetsize-32_altform-unplated", "targetsize-32", "scale-200", "scale-100" };

    /// <summary>A manifest logo reference → an existing file, exact name first, then qualified variants.</summary>
    private static string? ResolveLogoFile(string installLocation, string reference)
    {
        var rel = reference.Replace('/', '\\');
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installLocation)) + Path.DirectorySeparatorChar;
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, Path.GetDirectoryName(rel) ?? "")))
            + Path.DirectorySeparatorChar;
        // The reference comes from a file on disk: never let it point outside the package.
        if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(dir))
            return null;

        var baseName = Path.GetFileNameWithoutExtension(rel);
        var ext = Path.GetExtension(rel);
        var exact = Path.Combine(dir, baseName + ext);
        if (File.Exists(exact)) return exact;

        var candidates = Directory.EnumerateFiles(dir, baseName + ".*" + ext).ToList();
        foreach (var qualifier in PreferredQualifiers)
        {
            var match = candidates.FirstOrDefault(f =>
                Path.GetFileName(f).Contains("." + qualifier + ".", StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return candidates.FirstOrDefault();
    }

    /// <inheritdoc />
    public Task<long> MeasureSizeAsync(UwpApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        // Walks every file of the package: off the UI thread.
        return Task.Run(() => MeasureSize(app.InstallLocation));
    }

    /// <summary>
    /// Sum the install folder size (best-effort, guarded). File lengths come
    /// from the directory listing itself, so there is no extra stat per file.
    /// </summary>
    private static long MeasureSize(string location)
    {
        if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location)) return 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // One locked sub-folder must not zero the whole package.
            IgnoreInaccessible = true,
            // Count hidden and system files too, but never follow a link out of the package.
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        try
        {
            long total = 0;
            foreach (var file in new DirectoryInfo(location).EnumerateFiles("*", options))
                total += file.Length;
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Logger.Log("UWP size", "ERROR", $"{location}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Escape for a single-quoted PowerShell string literal.</summary>
    private static string Escape(string s) => s.Replace("'", "''", StringComparison.Ordinal);
}
