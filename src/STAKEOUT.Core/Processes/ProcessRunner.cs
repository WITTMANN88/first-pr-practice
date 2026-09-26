using System.Diagnostics;
using System.Text;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>What happens to a process that is still running when the caller stops waiting for it.</summary>
public enum AbandonPolicy
{
    /// <summary>Kill the whole process tree. For console tools: a hung powercfg is worse than a stopped one.</summary>
    Kill,

    /// <summary>
    /// Leave it running and keep draining its output in the background; its exit
    /// is logged. For installers: killing one mid-way can leave a broken program.
    /// </summary>
    Detach,
}

/// <summary>Result of an external process invocation.</summary>
/// <param name="ExitCode">The process exit code; -1 when it failed to start, timed out or was detached.</param>
/// <param name="StdOut">Captured standard output (so far, when detached).</param>
/// <param name="StdErr">Captured standard error, or the reason the run was abandoned.</param>
/// <param name="Detached">The caller stopped waiting and the process was left running (see <see cref="AbandonPolicy.Detach"/>).</param>
/// <param name="Completion">For a detached run: completes with the final exit code and the complete output once the process exits.</param>
public readonly record struct ProcessResult(
    int ExitCode, string StdOut, string StdErr, bool Detached = false, Task<ProcessResult>? Completion = null)
{
    public bool Success => ExitCode == 0 && !Detached;
}

/// <summary>
/// Runs console utilities (powercfg, sc, winget, powershell) without a visible
/// window and asynchronously, capturing stdout/stderr. Optionally the window can
/// be shown (used for GUI installers that must be visible).
/// </summary>
public static class ProcessRunner
{
    /// <summary>Default hard timeout for console utilities (ms).</summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>
    /// Run a program hidden and capture its output.
    /// When <paramref name="timeoutMs"/> expires or <paramref name="ct"/> is cancelled
    /// while the program still runs, <paramref name="onAbandon"/> decides its fate:
    /// <list type="bullet">
    /// <item><see cref="AbandonPolicy.Kill"/>: the process tree is killed; a timeout
    /// returns a failed result, a cancellation throws <see cref="OperationCanceledException"/>.</item>
    /// <item><see cref="AbandonPolicy.Detach"/>: the process keeps running and a result
    /// with <see cref="ProcessResult.Detached"/> is returned at once.</item>
    /// </list>
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName, string arguments,
        int timeoutMs = DefaultTimeoutMs, AbandonPolicy onAbandon = AbandonPolicy.Kill,
        CancellationToken ct = default)
    {
        // Security invariant: elevated tools run from an absolute path only (see SystemTools).
        if (!Path.IsPathFullyQualified(fileName))
            throw new ArgumentException($"'{fileName}' must be an absolute path.", nameof(fileName));

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
        return await RunInternalAsync(psi, timeoutMs, onAbandon, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Launch an installer/app so its own window appears on screen (no silent
    /// mode) and let the user drive it. Does not wait for exit.
    /// </summary>
    public static void LaunchVisible(string fileName, string arguments = "")
    {
        if (!Path.IsPathFullyQualified(fileName))
            throw new ArgumentException($"'{fileName}' must be an absolute path.", nameof(fileName));
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,   // let the shell show the installer UI
                CreateNoWindow = false,
            };
            // Dispose releases our handle only; the launched program keeps running.
            using var process = Process.Start(psi);
            Logger.Log("LaunchVisible", "OK", Path.GetFileName(fileName));
        }
        catch (Exception ex)
        {
            Logger.LogError("LaunchVisible " + fileName, ex);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Disposed in finally, or by WatchDetachedAsync once ownership passes to it on detach.")]
    private static async Task<ProcessResult> RunInternalAsync(
        ProcessStartInfo psi, int timeoutMs, AbandonPolicy onAbandon, CancellationToken ct)
    {
        var label = $"ProcessRunner {Path.GetFileName(psi.FileName)}";
        var output = new OutputBuffer();
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var detached = false;
        try
        {
            proc.OutputDataReceived += (_, e) => output.AppendOut(e.Data);
            proc.ErrorDataReceived += (_, e) => output.AppendErr(e.Data);

            if (!proc.Start())
                return new ProcessResult(-1, "", "process failed to start");

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Enforce a hard timeout on top of any external cancellation.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var reason = ct.IsCancellationRequested ? "cancelled by the user" : $"timeout after {timeoutMs} ms";
                if (onAbandon == AbandonPolicy.Detach)
                {
                    detached = true;
                    Logger.Log(label, "DETACHED", $"{reason}; left running in the background");
                    var completion = WatchDetachedAsync(proc, output, label);
                    return new ProcessResult(-1, output.Out, reason, Detached: true, Completion: completion);
                }

                TryKill(proc);
                if (ct.IsCancellationRequested) throw;
                Logger.Log(label, "TIMEOUT", $"exceeded {timeoutMs} ms; process killed");
                return new ProcessResult(-1, output.Out, reason);
            }

            return new ProcessResult(proc.ExitCode, output.Out, output.Err);
        }
        catch (OperationCanceledException)
        {
            throw; // external cancellation with AbandonPolicy.Kill
        }
        catch (Exception ex)
        {
            LogProcessError(psi.FileName, ex);
            return new ProcessResult(-1, "", ex.Message);
        }
        finally
        {
            // A detached process is owned (and disposed) by its watcher. Disposing
            // it here would close the output pipes, and a child that keeps writing
            // to a closed pipe can fail half-way (e.g. winget mid-install).
            if (!detached) proc.Dispose();
        }
    }

    /// <summary>
    /// Keeps the pipes drained until the detached process exits, then logs its
    /// exit code and disposes it. Never faults: failures become a -1 result.
    /// </summary>
    private static async Task<ProcessResult> WatchDetachedAsync(Process proc, OutputBuffer output, string label)
    {
        try
        {
            await proc.WaitForExitAsync().ConfigureAwait(false);
            Logger.Log(label, "DETACHED-EXIT", $"exit code {proc.ExitCode}");
            return new ProcessResult(proc.ExitCode, output.Out, output.Err);
        }
        catch (Exception ex)
        {
            Logger.LogError(label, ex);
            return new ProcessResult(-1, output.Out, ex.Message);
        }
        finally
        {
            proc.Dispose();
        }
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Logger.LogError("ProcessRunner.Kill", ex);
        }
    }

    private static void LogProcessError(string fileName, Exception ex)
    {
        var name = "ProcessRunner " + Path.GetFileName(fileName);
        // Win32 error 5 == ERROR_ACCESS_DENIED.
        if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 5 }
            || ex is UnauthorizedAccessException)
        {
            Logger.Log(name, "ACCESS_DENIED", ex.Message);
        }
        else
        {
            Logger.LogError(name, ex);
        }
    }

    /// <summary>
    /// stdout and stderr lines arrive on thread-pool threads, and a detached
    /// process keeps writing while the caller reads what it has so far.
    /// </summary>
    private sealed class OutputBuffer
    {
        private readonly StringBuilder _out = new();
        private readonly StringBuilder _err = new();

        public string Out
        {
            get { lock (_out) return _out.ToString(); }
        }

        public string Err
        {
            get { lock (_err) return _err.ToString(); }
        }

        public void AppendOut(string? line)
        {
            if (line is null) return;
            lock (_out) _out.AppendLine(line);
        }

        public void AppendErr(string? line)
        {
            if (line is null) return;
            lock (_err) _err.AppendLine(line);
        }
    }
}
