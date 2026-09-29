using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Stakeout.Infrastructure;

namespace Stakeout.Services;

/// <summary>
/// Starts a program as the signed-in user WITHOUT administrator rights, from
/// this elevated app: with a copy of the desktop shell's (explorer.exe) token.
///
/// Why: an HKCU Uninstall entry can be written by any program running as the
/// user, with no elevation. Running its UninstallString with STAKEOUT's admin
/// token would hand that program administrator rights the moment the user
/// clicks "remove". Per-user programs never need elevation to uninstall.
/// CreateProcessWithTokenW needs SeImpersonatePrivilege, which an elevated
/// administrator holds. If anything fails there is NO fallback to elevation.
/// </summary>
internal static class DesktopUserLauncher
{
    /// <summary>The started process (caller disposes), or null when it could not be started unelevated.</summary>
    public static DesktopUserProcess? Start(string fileName, string arguments, string? workingDirectory)
    {
        var shellWindow = NativeMethods.GetShellWindow();
        if (shellWindow == IntPtr.Zero) return null;
        if (NativeMethods.GetWindowThreadProcessId(shellWindow, out var shellPid) == 0 || shellPid == 0) return null;

        var shellProcess = IntPtr.Zero;
        var shellToken = IntPtr.Zero;
        var userToken = IntPtr.Zero;
        var app = IntPtr.Zero;
        var commandLine = IntPtr.Zero;
        var directory = IntPtr.Zero;
        try
        {
            shellProcess = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, 0, shellPid);
            if (shellProcess == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (NativeMethods.OpenProcessToken(shellProcess, NativeMethods.TokenDuplicate, out shellToken) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            const uint access = NativeMethods.TokenQuery | NativeMethods.TokenAssignPrimary | NativeMethods.TokenDuplicate
                | NativeMethods.TokenAdjustDefault | NativeMethods.TokenAdjustSessionId;
            if (NativeMethods.DuplicateTokenEx(shellToken, access, IntPtr.Zero,
                    NativeMethods.SecurityImpersonation, NativeMethods.TokenPrimary, out userToken) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            app = Marshal.StringToHGlobalUni(fileName);
            commandLine = Marshal.StringToHGlobalUni($"\"{fileName}\" {arguments}".TrimEnd());
            directory = string.IsNullOrEmpty(workingDirectory) ? IntPtr.Zero : Marshal.StringToHGlobalUni(workingDirectory);
            var startup = new NativeMethods.StartupInfo { Size = Marshal.SizeOf<NativeMethods.StartupInfo>() };
            if (NativeMethods.CreateProcessWithTokenW(userToken, 0, app, commandLine, 0, IntPtr.Zero, directory,
                    ref startup, out var info) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            Close(info.Thread);
            return new DesktopUserProcess(info.Process, info.ProcessId);
        }
        catch (Win32Exception ex)
        {
            Stakeout.Core.Logger.Log("Apps.Uninstall", "BLOCKED",
                $"could not start {Path.GetFileName(fileName)} without elevation ({ex.NativeErrorCode}: {ex.Message}); not run elevated");
            return null;
        }
        finally
        {
            Free(app);
            Free(commandLine);
            Free(directory);
            Close(userToken);
            Close(shellToken);
            Close(shellProcess);
        }
    }

    private static void Free(IntPtr p)
    {
        if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
    }

    /// <summary>Best effort: a handle that fails to close leaks nothing we can act on.</summary>
    private static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero) _ = NativeMethods.CloseHandle(handle);
    }
}

/// <summary>A process started by <see cref="DesktopUserLauncher"/>: its PID and a waitable handle.</summary>
internal sealed class DesktopUserProcess : IDisposable
{
    private readonly ProcessHandleWait _handle;

    public DesktopUserProcess(IntPtr processHandle, int id)
    {
        _handle = new ProcessHandleWait(processHandle);
        Id = id;
    }

    public int Id { get; }

    /// <summary>Completes when the process exits (the handle stays valid even if it already has).</summary>
    public async Task WaitForExitAsync(CancellationToken ct)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(_handle, static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
            done, Timeout.Infinite, executeOnlyOnce: true);
        try
        {
            using (ct.Register(() => done.TrySetCanceled(ct)))
                await done.Task.ConfigureAwait(false);
        }
        finally
        {
            registration.Unregister(null);
        }
    }

    public void Dispose() => _handle.Dispose();

    private sealed class ProcessHandleWait : WaitHandle
    {
        public ProcessHandleWait(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
    }
}
