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
    /// <summary>Default hard timeout for console utilities (ms).</summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>
    /// Run a program hidden and capture its output. If it runs longer than
    /// <paramref name="timeoutMs"/> it is killed and a TIMEOUT result returned,
    /// so a hung external tool can never stall the caller indefinitely.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName, string arguments,
        CancellationToken ct = default, int timeoutMs = DefaultTimeoutMs)
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
        return await RunInternalAsync(psi, ct, timeoutMs);
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
        ProcessStartInfo psi, CancellationToken ct, int timeoutMs)
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

            // Enforce a hard timeout on top of any external cancellation.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out (not user-cancelled): kill the whole process tree.
                TryKill(proc);
                Logger.Log($"ProcessRunner {Path.GetFileName(psi.FileName)}", "TIMEOUT",
                    $"exceeded {timeoutMs} ms; process killed");
                return new ProcessResult(-1, sbOut.ToString(), $"timeout after {timeoutMs} ms");
            }

            return new ProcessResult(proc.ExitCode, sbOut.ToString(), sbErr.ToString());
        }
        catch (OperationCanceledException)
        {
            // External cancellation requested by the caller.
            throw;
        }
        catch (Exception ex)
        {
            LogProcessError(psi.FileName, ex);
            return new ProcessResult(-1, "", ex.Message);
        }
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }

    private static void LogProcessError(string fileName, Exception ex)
    {
        var name = "ProcessRunner " + Path.GetFileName(fileName);
        // Win32 error 5 == ERROR_ACCESS_DENIED.
        if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 5 }
            || ex is UnauthorizedAccessException)
            Logger.Log(name, "ACCESS_DENIED", ex.Message);
        else
            Logger.LogError(name, ex);
    }
}
