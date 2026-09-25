using System.Diagnostics;
using System.IO;
using System.Text;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>Result of an external process invocation.</summary>
public readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Runs console utilities (powercfg, sc, bcdedit, winget, powershell) without a
/// visible window and asynchronously, capturing stdout/stderr. Optionally the
/// window can be shown (used for GUI installers that must be visible).
/// </summary>
public static class ProcessRunner
{
    /// <summary>Run a program hidden and capture its output.</summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName, string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        return await RunInternalAsync(psi, ct);
    }

    /// <summary>
    /// Launch an installer/app so its own window appears on screen (no silent
    /// mode) and let the user drive it. Does not wait for exit.
    /// </summary>
    public static void LaunchVisible(string fileName, string arguments = "")
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,   // let the shell show the installer UI
                CreateNoWindow = false,
            };
            Process.Start(psi);
            Logger.Log("LaunchVisible", "OK", Path.GetFileName(fileName));
        }
        catch (Exception ex)
        {
            Logger.LogError("LaunchVisible " + fileName, ex);
        }
    }

    private static async Task<ProcessResult> RunInternalAsync(
        ProcessStartInfo psi, CancellationToken ct)
    {
        try
        {
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

            if (!proc.Start())
                return new ProcessResult(-1, "", "process failed to start");

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);
            return new ProcessResult(proc.ExitCode, sbOut.ToString(), sbErr.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogError($"ProcessRunner {psi.FileName}", ex);
            return new ProcessResult(-1, "", ex.Message);
        }
    }
}
