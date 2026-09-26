namespace Stakeout.Core;

/// <summary>
/// Resolves a bare executable name ("winget.exe") to an absolute path without
/// the Windows CreateProcess search order. That order starts with the directory
/// of the running exe and the current directory, so an elevated app starting
/// "sc.exe" by name would run a planted sc.exe sitting next to STAKEOUT.exe
/// (typically in Downloads) with administrator rights.
///
/// Only the given trusted directories and the fully qualified entries of PATH are
/// searched; relative PATH entries (".", "tools") are skipped because they
/// resolve against the current directory.
/// </summary>
public static class ExecutableResolver
{
    /// <returns>The first existing candidate, or null when the tool is not installed.</returns>
    public static string? Resolve(
        string fileName,
        IEnumerable<string> trustedDirectories,
        string? pathVariable,
        Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(trustedDirectories);
        ArgumentNullException.ThrowIfNull(fileExists);
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            throw new ArgumentException("A bare file name is expected, not a path.", nameof(fileName));

        foreach (var directory in trustedDirectories.Concat(SplitPath(pathVariable)))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, fileName);
            if (fileExists(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<string> SplitPath(string? pathVariable)
        => (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Trim('"'))
            .Where(entry => entry.Length > 0);
}
