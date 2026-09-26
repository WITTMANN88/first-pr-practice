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
    public static IReadOnlyList<UwpApp> Parse(string? stdout)
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

        Disambiguate(apps);
        return apps
            .OrderBy(a => a.Category)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Packages that share an identity name (frameworks installed for x64 and
    /// x86, or in two versions side by side) would show as identical rows: append
    /// what tells them apart, "(x86)" or "(x64, 8000.616.304.0)".
    /// </summary>
    private static void Disambiguate(List<UwpApp> apps)
    {
        foreach (var group in apps.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var showArch = group.Select(a => a.Architecture).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            foreach (var app in group)
            {
                var showVersion = group.Count(o => string.Equals(o.Architecture, app.Architecture, StringComparison.OrdinalIgnoreCase)) > 1;
                var parts = new List<string>(2);
                if (showArch && app.Architecture.Length > 0) parts.Add(app.Architecture);
                if (showVersion && app.Version.Length > 0) parts.Add(app.Version);
                if (parts.Count > 0) app.DisplayName += $" ({string.Join(", ", parts)})";
            }
        }
    }
}
