using Observation.Core.SystemAccess;

namespace Observation.Handlers.Security;

/// <summary>
/// Controlled Folder Access — Set-MpPreference -EnableControlledFolderAccess Enabled/Disabled.
/// Важная смычка из плана: включение этого твика без добавления Observation.exe в разрешённые
/// CFA-приложения рискует заблокировать саму программу при следующем запуске (запись в
/// Observation_Data и т.п.) — поэтому onScript сначала добавляет текущий исполняемый файл в
/// Add-MpPreference -ControlledFolderAccessAllowedApplications (аддитивная, идемпотентная
/// команда — не трогает уже разрешённые сторонние приложения) и только потом включает CFA.
/// </summary>
public static class ControlledFolderAccessHandler
{
    public static PowerShellToggleHandler Create(ICommandRunner runner)
    {
        var exePath = Environment.ProcessPath;
        var allowSelf = string.IsNullOrEmpty(exePath)
            ? string.Empty
            : $"Add-MpPreference -ControlledFolderAccessAllowedApplications '{exePath.Replace("'", "''")}'; ";

        return new PowerShellToggleHandler(
            runner,
            onScript: allowSelf + "Set-MpPreference -EnableControlledFolderAccess Enabled",
            offScript: "Set-MpPreference -EnableControlledFolderAccess Disabled",
            verifyScript: "(Get-MpPreference).EnableControlledFolderAccess -eq 1");
    }
}
