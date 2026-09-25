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
                try { p.Kill(); } catch { /* ignore individual failures */ }
            }
            await Task.Delay(800);
            // If the shell did not come back on its own, start it.
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            Logger.Log("Explorer", "RESTARTED");
        }
        catch (Exception ex)
        {
            Logger.LogError("RestartExplorer", ex);
        }
    }
}
