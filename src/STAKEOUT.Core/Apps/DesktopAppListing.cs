using System.Globalization;
using System.Text.RegularExpressions;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>One subkey of an Uninstall branch: its name and its values as read.</summary>
/// <param name="Source">Branch the key was found in.</param>
/// <param name="KeyName">Subkey name: a product code ("{GUID}"), a name, or "KB…" for updates.</param>
/// <param name="Values">Value name → value (string, int for DWORD, ...), case-insensitive names.</param>
public sealed record UninstallEntry(UninstallSource Source, string KeyName, IReadOnlyDictionary<string, object> Values);

/// <summary>
/// Turns the Uninstall entries of the registry into the desktop programs a user
/// would recognise and can remove, the way Windows' "Programs and Features" does:
///   * needs a DisplayName and an uninstall command (UninstallString, or
///     QuietUninstallString when that is all there is);
///   * hides system components (SystemComponent = 1);
///   * hides updates: keys named "KB" + number, entries that belong to a parent
///     product (ParentKeyName), and ReleaseType Update / Hotfix / Security Update;
///   * lists a program once even if several branches carry it (same name and
///     version): 64-bit HKLM first, then 32-bit, then the current user's.
/// Pure: the registry reading happens in the app, so this is unit-tested.
/// </summary>
public static partial class DesktopAppListing
{
    private static readonly string[] UpdateReleaseTypes = { "Update", "Hotfix", "Security Update", "Service Pack" };

    [GeneratedRegex(@"^KB\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UpdateKey();

    public static IReadOnlyList<InstalledApp> Parse(IEnumerable<UninstallEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<InstalledApp>();
        foreach (var entry in entries.OrderBy(e => e.Source))
        {
            if (!IsListed(entry)) continue;
            var app = ToApp(entry);
            if (seen.Add(app.DisplayName + "\u0001" + app.Version)) apps.Add(app);
        }
        return apps.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Whether Programs and Features would show this entry (see the class remarks).</summary>
    public static bool IsListed(UninstallEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var v = entry.Values;
        if (string.IsNullOrWhiteSpace(Text(v, "DisplayName"))) return false;
        if (string.IsNullOrWhiteSpace(Text(v, "UninstallString")) && string.IsNullOrWhiteSpace(Text(v, "QuietUninstallString"))) return false;
        if (Number(v, "SystemComponent") == 1) return false;
        if (UpdateKey().IsMatch(entry.KeyName)) return false;
        if (!string.IsNullOrWhiteSpace(Text(v, "ParentKeyName"))) return false;
        var releaseType = Text(v, "ReleaseType");
        return !UpdateReleaseTypes.Any(t => string.Equals(t, releaseType, StringComparison.OrdinalIgnoreCase));
    }

    private static InstalledApp ToApp(UninstallEntry entry)
    {
        var v = entry.Values;
        var icon = DisplayIconParser.Parse(Text(v, "DisplayIcon"));
        var sizeKb = Number(v, "EstimatedSize") ?? 0;
        var uninstall = Text(v, "UninstallString");
        return new InstalledApp
        {
            Kind = AppKind.Desktop,
            Category = AppCategory.Desktop,
            Source = entry.Source,
            Name = entry.KeyName,
            DisplayName = Text(v, "DisplayName")!.Trim(),
            Publisher = Text(v, "Publisher")?.Trim() ?? "",
            Version = Text(v, "DisplayVersion")?.Trim() ?? "",
            InstallLocation = Text(v, "InstallLocation")?.Trim().Trim('"') ?? "",
            IconPath = icon?.Path,
            IconIndex = icon?.Index ?? 0,
            // Interactive uninstaller first: the user sees and confirms what is removed.
            UninstallCommand = (string.IsNullOrWhiteSpace(uninstall) ? Text(v, "QuietUninstallString") : uninstall)!.Trim(),
            SizeBytes = sizeKb > 0 ? sizeKb * 1024L : 0,
        };
    }

    private static string? Text(IReadOnlyDictionary<string, object> values, string name)
        => values.TryGetValue(name, out var value) ? value as string : null;

    /// <summary>DWORDs arrive as int; some installers write numbers as strings.</summary>
    private static long? Number(IReadOnlyDictionary<string, object> values, string name)
    {
        if (!values.TryGetValue(name, out var value)) return null;
        return value switch
        {
            int i => unchecked((uint)i),
            long l => l,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
    }
}

/// <summary>DisplayIcon: "C:\App\app.exe,0", "\"C:\A b\app.exe\",-101", or a plain .ico path.</summary>
public static class DisplayIconParser
{
    /// <summary>Path (env vars not expanded) and icon index; null when there is no path.</summary>
    public static (string Path, int Index)? Parse(string? displayIcon)
    {
        var text = displayIcon?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        string path;
        var rest = "";
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close < 0) return null;
            path = text[1..close];
            rest = text[(close + 1)..];
        }
        else
        {
            // The index follows the last comma, but only if what follows is a number.
            var comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            {
                path = text[..comma];
                rest = text[comma..];
            }
            else
            {
                path = text;
            }
        }

        var index = 0;
        rest = rest.Trim();
        if (rest.Length > 1 && rest[0] == ','
            && !int.TryParse(rest[1..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out index))
        {
            index = 0;
        }

        path = path.Trim();
        return path.Length == 0 ? null : (path, index);
    }
}
