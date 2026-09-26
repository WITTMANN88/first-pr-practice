using System.Diagnostics;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>Small, self-contained system actions used by the UI.</summary>
public static class SystemActions
{
    /// <summary>
    /// Restart explorer.exe so shell-visual tweaks (animations, MenuShowDelay)
    /// take effect without a full reboot. Killing explorer also closes the
    /// desktop momentarily; Windows relaunches it, and we start it explicitly to
    /// be safe on editions where it does not auto-restart.
    /// </summary>
    public static async Task RestartExplorerAsync()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                // Each Process wraps an OS handle: release it deterministically.
                using (p)
                {
                    try { p.Kill(); } catch { /* ignore individual failures */ }
                }
            }
            await Task.Delay(800);
            // If the shell did not come back on its own, start it (absolute path:
            // never let a planted explorer.exe in the working directory win).
            if (!IsExplorerRunning())
            {
                using var shell = Process.Start(new ProcessStartInfo(SystemTools.Explorer) { UseShellExecute = true });
            }
            Logger.Log("Explorer", "RESTARTED");
        }
        catch (Exception ex)
        {
            Logger.LogError("RestartExplorer", ex);
        }
    }

    private static bool IsExplorerRunning()
    {
        var running = Process.GetProcessesByName("explorer");
        foreach (var p in running) p.Dispose();
        return running.Length > 0;
    }
}
