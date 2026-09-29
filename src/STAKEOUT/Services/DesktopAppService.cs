using System.Diagnostics;
using System.IO;
using System.Management;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>How an uninstall ended, judged by whether the program's Uninstall entry is gone.</summary>
public enum UninstallOutcome
{
    /// <summary>The uninstaller finished and the program is no longer registered.</summary>
    Removed,
    /// <summary>The uninstaller finished but the program is still registered (cancelled, or still running elsewhere).</summary>
    StillInstalled,
    /// <summary>No usable uninstall command, or it could not be started.</summary>
    NotStarted,
}

/// <summary>Desktop (Win32) programs: listing from the registry and removal through their own uninstallers.</summary>
public interface IDesktopAppService
{
    /// <summary>Programs from the three Uninstall branches, as "Programs and Features" shows them.</summary>
    Task<IReadOnlyList<InstalledApp>> ListAsync();

    /// <summary>Run the program's uninstaller (its own UI) and wait for it and the processes it started.</summary>
    Task<UninstallOutcome> UninstallAsync(InstalledApp app, CancellationToken ct = default);
}

/// <summary>
/// Reads HKLM (64- and 32-bit views) and HKCU Uninstall keys; filtering lives in
/// <see cref="DesktopAppListing"/>, command parsing in <see cref="UninstallCommand"/>.
/// Uninstallers run with their own UI (the interactive UninstallString): the
/// user confirms and sees what is removed. HKCU (per-user) uninstallers run
/// without elevation (<see cref="DesktopUserLauncher"/>).
/// </summary>
public sealed class DesktopAppService : IDesktopAppService
{
    private static readonly (UninstallSource Source, RegHive Hive, string Path)[] Branches =
    {
        (UninstallSource.LocalMachine, RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (UninstallSource.LocalMachine32, RegHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (UninstallSource.CurrentUser, RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
    };

    /// <summary>System tools an UninstallString may name bare: looked up here only.</summary>
    private static readonly string[] SystemDirectories =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    };

    public Task<IReadOnlyList<InstalledApp>> ListAsync() => Task.Run(() =>
    {
        var entries = Branches.SelectMany(b => RegistryHelper.SubKeyNames(b.Hive, b.Path)
            .Select(key => new UninstallEntry(b.Source, key, RegistryHelper.ReadValues(b.Hive, $@"{b.Path}\{key}"))));
        var apps = DesktopAppListing.Parse(entries);
        foreach (var app in apps)
        {
            if (app.IconPath != null) app.IconPath = Environment.ExpandEnvironmentVariables(app.IconPath);
            app.InstallLocation = Environment.ExpandEnvironmentVariables(app.InstallLocation);
        }
        Logger.Log("Apps.Desktop", "OK", $"{apps.Count} program(s)");
        return apps;
    });

    public async Task<UninstallOutcome> UninstallAsync(InstalledApp app, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        var command = UninstallCommand.Parse(Environment.ExpandEnvironmentVariables(app.UninstallCommand), File.Exists);
        var file = command is null ? null : Resolve(command);
        if (command is null || file is null)
        {
            Logger.Log("Apps.Uninstall", "FAILED", $"{app.DisplayName}: no usable uninstall command ({app.UninstallCommand})");
            return UninstallOutcome.NotStarted;
        }

        Logger.Log("Apps.Uninstall", "START",
            $"{app.DisplayName}: {file} {command.Arguments}".TrimEnd() + (app.IsPerUser ? " (as the user, not elevated)" : ""));
        var directory = Path.GetDirectoryName(file);
        int pid;
        if (app.IsPerUser)
        {
            using var process = DesktopUserLauncher.Start(file, command.Arguments, directory);
            if (process is null) return UninstallOutcome.NotStarted;
            pid = process.Id;
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        else
        {
            using var process = StartElevated(file, command.Arguments, directory);
            if (process is null) return UninstallOutcome.NotStarted;
            pid = process.Id;
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }

        // Many uninstallers copy themselves to TEMP, start the copy and exit at once
        // (NSIS, Inno Setup): wait for what they started, too — but only their
        // helpers, not e.g. a browser opened on a feedback page.
        await WaitForHelpersAsync(pid, directory, depth: 2, ct).ConfigureAwait(false);

        var branch = Branches.First(b => b.Source == app.Source);
        var removed = !RegistryHelper.KeyExists(branch.Hive, $@"{branch.Path}\{app.Name}");
        Logger.Log("Apps.Uninstall", removed ? "OK" : "PARTIAL",
            removed ? app.DisplayName : $"{app.DisplayName}: still registered after the uninstaller closed");
        return removed ? UninstallOutcome.Removed : UninstallOutcome.StillInstalled;
    }

    /// <summary>A full path that exists, or a bare system tool found in System32 / Windows only.</summary>
    private static string? Resolve(UninstallCommand command)
    {
        if (!command.IsSystemTool) return File.Exists(command.FileName) ? command.FileName : null;
        return ExecutableResolver.Resolve(command.FileName, SystemDirectories, pathVariable: null, File.Exists);
    }

    private static Process? StartElevated(string file, string arguments, string? directory)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = directory ?? "",
            });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Logger.LogError("Apps.Uninstall " + Path.GetFileName(file), ex);
            return null;
        }
    }

    /// <summary>
    /// Children of <paramref name="parentId"/> (and theirs, up to <paramref name="depth"/>
    /// levels) that are uninstaller helpers: started from TEMP or from the
    /// uninstaller's own folder.
    /// </summary>
    private static async Task WaitForHelpersAsync(int parentId, string? uninstallerDirectory, int depth, CancellationToken ct)
    {
        if (depth <= 0) return;
        foreach (var (childId, path) in ChildProcesses(parentId))
        {
            if (!IsHelper(path, uninstallerDirectory)) continue;
            Process child;
            try
            {
                child = Process.GetProcessById(childId);
            }
            catch (ArgumentException)
            {
                continue; // already gone
            }
            using (child)
            {
                await WaitForHelpersAsync(childId, uninstallerDirectory, depth - 1, ct).ConfigureAwait(false);
                await child.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsHelper(string? executablePath, string? uninstallerDirectory)
    {
        if (string.IsNullOrEmpty(executablePath)) return false;
        var directory = Path.GetDirectoryName(executablePath) ?? "";
        return directory.StartsWith(Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(uninstallerDirectory)
                && directory.StartsWith(uninstallerDirectory, StringComparison.OrdinalIgnoreCase));
    }

    private static List<(int Id, string? Path)> ChildProcesses(int parentId)
    {
        var children = new List<(int, string?)>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2",
                $"SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE ParentProcessId = {parentId}", Wmi.Options(TimeSpan.FromSeconds(10)));
            Wmi.ForEach(searcher, item =>
            {
                if (item["ProcessId"] is uint id) children.Add(((int)id, item["ExecutablePath"] as string));
                return true;
            });
        }
        catch (ManagementException ex)
        {
            Logger.LogError("Apps.Uninstall children", ex);
        }
        return children;
    }
}
