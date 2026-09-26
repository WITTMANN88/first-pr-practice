using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Stakeout.Core;

namespace Stakeout.Infrastructure;

/// <summary>
/// Locks %ProgramData%\STAKEOUT down to SYSTEM and Administrators.
///
/// Why: ProgramData lets standard users create files in new subfolders, and
/// tweak-state.json (and its backups) holds registry keys and values that
/// "Revert all" writes back with administrator rights. A file planted there by
/// an unprivileged user would therefore be a privilege escalation. The folder
/// gets a protected DACL (no inherited "Users" rights); files that already
/// exist are re-owned by Administrators and stripped of explicit rights, so a
/// pre-planted file cannot keep write access through its owner.
/// Best effort: failures are logged, never fatal.
/// </summary>
public static class StateDirectorySecurity
{
    public static void Harden(string directory)
    {
        try
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            var folder = Directory.CreateDirectory(directory);
            var security = new DirectorySecurity();
            security.SetOwner(admins);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { system, admins })
            {
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }
            folder.SetAccessControl(security);

            foreach (var entry in folder.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                ResetEntry(entry, admins);

            Logger.Log("StateDirectory", "OK", "restricted to SYSTEM and Administrators");
        }
        catch (Exception ex)
        {
            Logger.LogError("StateDirectory.Harden", ex);
        }
    }

    /// <summary>Owner → Administrators; only the rights inherited from the locked folder remain.</summary>
    private static void ResetEntry(FileSystemInfo entry, SecurityIdentifier admins)
    {
        try
        {
            switch (entry)
            {
                case FileInfo file:
                    var fileSecurity = new FileSecurity();
                    fileSecurity.SetOwner(admins);
                    fileSecurity.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                    file.SetAccessControl(fileSecurity);
                    break;
                case DirectoryInfo dir:
                    var dirSecurity = new DirectorySecurity();
                    dirSecurity.SetOwner(admins);
                    dirSecurity.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                    dir.SetAccessControl(dirSecurity);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("StateDirectory.Reset " + entry.Name, ex);
        }
    }
}
