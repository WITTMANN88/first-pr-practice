using Observation.Core.SystemAccess;

namespace Observation.Handlers.Security;

/// <summary>Файрвол по всем трём профилям — Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled True/False.</summary>
public static class FirewallProfileHandler
{
    public static PowerShellToggleHandler Create(ICommandRunner runner) => new(
        runner,
        onScript: "Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled True",
        offScript: "Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False",
        verifyScript: "(Get-NetFirewallProfile -Profile Public).Enabled -eq 'True'");
}
