using System.Text.RegularExpressions;

namespace Stakeout.Services;

/// <summary>
/// An UninstallString split into the program to start and its arguments.
/// <see cref="FileName"/> is either a full path or a bare system tool name
/// ("msiexec.exe", "rundll32.exe") that the caller resolves in System32 only,
/// never through the current directory or PATH order.
/// </summary>
public sealed partial record UninstallCommand(string FileName, string Arguments)
{
    private static readonly System.Buffers.SearchValues<char> Whitespace = System.Buffers.SearchValues.Create(" \t");

    [GeneratedRegex(@"^msiexec(?:\.exe)?(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MsiExecPrefix();

    // "/I{GUID}" or "/I {GUID}" (install / repair) → "/X{GUID}" (uninstall).
    [GeneratedRegex(@"(?<=^|\s)[/-]i(?=\s*\{)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MsiInstallSwitch();

    /// <summary>True when <see cref="FileName"/> is a bare name to resolve in System32.</summary>
    public bool IsSystemTool => !IsFullyQualified(FileName);

    /// <summary>
    /// Windows rules, whatever OS runs this code (the input is always a Windows
    /// registry string): "C:\…" or a UNC "\server\…".
    /// </summary>
    public static bool IsFullyQualified(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path.Length > 2;
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
    }

    private static bool HasDirectory(string name) => name.Contains('\\', StringComparison.Ordinal) || name.Contains('/', StringComparison.Ordinal);

    /// <summary>
    /// Split <paramref name="commandLine"/> (environment variables already expanded):
    ///   "C:\Program Files\App\uninst.exe" /S      quoted path + arguments
    ///   MsiExec.exe /I{GUID}                     msiexec, with /I turned into /X
    ///   C:\Program Files\App\uninst.exe /S       unquoted path with spaces: the
    ///                                            longest ".exe" prefix that exists
    ///   RunDll32 advpack.dll,LaunchINFSection    bare system tool
    /// Null when no program can be identified.
    /// </summary>
    public static UninstallCommand? Parse(string? commandLine, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        var text = commandLine?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        if (MsiExecPrefix().Match(text) is { Success: true } msi)
        {
            var args = MsiInstallSwitch().Replace(text[msi.Length..].Trim(), "/X");
            return new UninstallCommand("msiexec.exe", args);
        }

        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close <= 1) return null;
            var file = text[1..close];
            return Accept(file, text[(close + 1)..], fileExists);
        }

        // Unquoted: a full path may contain spaces. Try every ".exe" end, shortest
        // first, and take the first that exists ("C:\Program Files\A B\u.exe /x").
        if (IsFullyQualified(text))
        {
            for (var at = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); at >= 0;
                 at = text.IndexOf(".exe", at + 1, StringComparison.OrdinalIgnoreCase))
            {
                var end = at + 4;
                if (end < text.Length && !char.IsWhiteSpace(text[end])) continue;
                var file = text[..end];
                if (fileExists(file)) return new UninstallCommand(file, text[end..].Trim());
            }
            return null;
        }

        // A bare tool name: "RunDll32 x.dll,Entry", "rundll32.exe ...".
        var space = text.AsSpan().IndexOfAny(Whitespace);
        var name = space < 0 ? text : text[..space];
        if (HasDirectory(name)) return null;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        return new UninstallCommand(name, space < 0 ? "" : text[space..].Trim());
    }

    private static UninstallCommand? Accept(string file, string args, Func<string, bool> fileExists)
    {
        if (IsFullyQualified(file)) return fileExists(file) ? new UninstallCommand(file, args.Trim()) : null;
        // Quoted bare name ("msiexec" is handled above): only a plain file name is acceptable.
        if (HasDirectory(file)) return null;
        var name = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : file + ".exe";
        return new UninstallCommand(name, args.Trim());
    }
}
