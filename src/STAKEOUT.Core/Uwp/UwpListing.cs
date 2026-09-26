using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Parses the output of the listing script
/// <c>Get-AppxPackage -AllUsers | ForEach-Object { "$($_.Name)|$($_.PackageFullName)|$($_.InstallLocation)" }</c>
/// into <see cref="UwpApp"/> entries.
/// </summary>
public static class UwpListing
{
    /// <summary>
    /// One entry per distinct PackageFullName (-AllUsers repeats packages per
    /// user), categorised and sorted by category (junk first, system last), then
    /// by display name. Malformed lines are skipped.
    /// </summary>
    public static List<UwpApp> Parse(string? stdout)
    {
        var apps = new List<UwpApp>();
        if (string.IsNullOrWhiteSpace(stdout)) return apps;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Trim().Split('|');
            if (parts.Length < 2) continue;

            var name = parts[0].Trim();
            var full = parts[1].Trim();
            if (name.Length == 0 || full.Length == 0 || !seen.Add(full)) continue;

            var location = parts.Length > 2 ? parts[2].Trim() : "";
            apps.Add(UwpApp.FromIdentity(name, full, location));
        }

        return apps
            .OrderBy(a => a.Category)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
