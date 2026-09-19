using Observation.Core.SystemAccess;

namespace Observation.Handlers.Diag;

/// <summary>
/// Классическое меню F8 (Advanced Boot Options) вместо современного экрана восстановления —
/// bcdedit /set {current} bootmenupolicy Legacy/Standard (см. tweaks.sample.json, diag.legacybootmenu).
/// </summary>
public static class LegacyBootMenuHandler
{
    public static PowerShellToggleHandler Create(ICommandRunner runner) => new(
        runner,
        onScript: "bcdedit /set {current} bootmenupolicy Legacy",
        offScript: "bcdedit /set {current} bootmenupolicy Standard",
        verifyScript: "(bcdedit /enum '{current}' | Select-String 'bootmenupolicy\\s+Legacy') -ne $null");
}
