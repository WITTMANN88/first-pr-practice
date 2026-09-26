namespace Stakeout.Services;

/// <summary>
/// Picks the file behind a manifest logo reference ("Assets\Square44x44Logo.png").
/// Packages rarely ship that exact file: the name carries resource qualifiers
/// ("Square44x44Logo.targetsize-32_altform-unplated.png") or the file sits in a
/// qualifier folder ("Assets\scale-200\Square44x44Logo.png"). Pure path logic,
/// so it is unit-tested; the service only lists the files.
/// </summary>
public static class UwpLogoPicker
{
    /// <summary>32 px unplated (drawn for dark rows) first, then 32 px, then scale variants.</summary>
    private static readonly string[] PreferredQualifiers =
        { "targetsize-32_altform-unplated", "targetsize-32", "scale-200", "scale-100" };

    /// <summary>
    /// Best variant among <paramref name="relativePaths"/> (relative to the
    /// reference's folder, '\'-separated), or null when none is a variant.
    /// Order: the exact file, a preferred qualifier, anything but a
    /// high-contrast variant, anything.
    /// </summary>
    public static string? Pick(IEnumerable<string> relativePaths, string baseName, string extension)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        var variants = relativePaths.Where(p => IsVariant(p, baseName, extension)).ToList();
        if (variants.Count == 0) return null;

        var exact = variants.FirstOrDefault(p => string.Equals(p, baseName + extension, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        foreach (var qualifier in PreferredQualifiers)
        {
            var match = variants.FirstOrDefault(p => Tokens(p).Contains(qualifier, StringComparer.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return variants.FirstOrDefault(p => !p.Contains("contrast-", StringComparison.OrdinalIgnoreCase))
            ?? variants[0];
    }

    /// <summary>"Logo.png", "Logo.scale-200.png" or "scale-200\Logo.png" for base "Logo" and ".png".</summary>
    public static bool IsVariant(string relativePath, string baseName, string extension)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var name = relativePath[(relativePath.LastIndexOf('\\') + 1)..];
        if (string.Equals(name, baseName + extension, StringComparison.OrdinalIgnoreCase)) return true;
        return name.Length > baseName.Length + 1 + extension.Length
            && name.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly char[] TokenSeparators = { '\\', '.' };

    /// <summary>Folder names and dot-separated name parts: the places qualifiers appear.</summary>
    private static string[] Tokens(string relativePath) => relativePath.Split(TokenSeparators);
}
