using System.Security.Principal;

namespace Stakeout.Core;

/// <summary>Elevation checks. The manifest forces elevation, but we verify.</summary>
public static class PrivilegeHelper
{
    /// <summary>True when the current process is running with an elevated token.</summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
