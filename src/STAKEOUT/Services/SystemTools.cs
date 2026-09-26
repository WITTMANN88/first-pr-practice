using System.IO;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>
/// Absolute paths of the external tools STAKEOUT runs with administrator rights.
/// Never start them by bare name: Windows would look in STAKEOUT.exe's own folder
/// and the current directory first, so a planted "powercfg.exe" next to the exe
/// would run elevated (see <see cref="ExecutableResolver"/>).
/// </summary>
public static class SystemTools
{
    private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

    public static string PowerShell => Path.Combine(System32, "WindowsPowerShell", "v1.0", "powershell.exe");
    public static string Sc => Path.Combine(System32, "sc.exe");
    public static string PowerCfg => Path.Combine(System32, "powercfg.exe");
    public static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>
    /// winget is a per-user App Execution Alias (%LOCALAPPDATA%\Microsoft\WindowsApps),
    /// not a System32 binary: look there, then in absolute PATH entries. Null if absent.
    /// </summary>
    public static string? Winget => ExecutableResolver.Resolve(
        "winget.exe",
        new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps") },
        Environment.GetEnvironmentVariable("PATH"),
        File.Exists);
}
