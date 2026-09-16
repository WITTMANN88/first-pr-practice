using Observation.Core.SystemAccess;

namespace Observation.Handlers.Security;

/// <summary>
/// Controlled Folder Access — Set-MpPreference -EnableControlledFolderAccess Enabled/Disabled.
/// Важная смычка из плана: если это включить, а Observation.exe не добавлен в разрешённые
/// CFA-приложения, программа рискует заблокировать сама себя при следующем запуске — сама
/// эта смычка (авто-добавление себя в исключения) не реализована в этом заходе, только твик.
/// </summary>
public static class ControlledFolderAccessHandler
{
    public static PowerShellToggleHandler Create(ICommandRunner runner) => new(
        runner,
        onScript: "Set-MpPreference -EnableControlledFolderAccess Enabled",
        offScript: "Set-MpPreference -EnableControlledFolderAccess Disabled",
        verifyScript: "(Get-MpPreference).EnableControlledFolderAccess -eq 1");
}
