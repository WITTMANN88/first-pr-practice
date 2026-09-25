using System.Text;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>
/// Executes PowerShell scripts asynchronously and hidden. Used for UWP package
/// management (Get/Remove-AppxPackage) and any tweak better expressed in PS.
///
/// Scripts are passed as a Base64-encoded command (-EncodedCommand) so quoting
/// and special characters never break the invocation.
/// </summary>
public static class PowerShellRunner
{
    /// <summary>Run a script block hidden with the execution policy bypassed.</summary>
    public static Task<ProcessResult> RunScriptAsync(string script, CancellationToken ct = default)
    {
        // -EncodedCommand expects UTF-16LE Base64.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";
        Logger.Log("PowerShell", "RUN", Truncate(script));
        return ProcessRunner.RunAsync("powershell.exe", args, ct);
    }

    private static string Truncate(string s)
        => s.Length <= 80 ? s.Replace('\n', ' ') : s[..80].Replace('\n', ' ') + "...";
}
