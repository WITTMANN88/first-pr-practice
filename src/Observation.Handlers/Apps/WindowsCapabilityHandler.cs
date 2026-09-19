using Observation.Core.SystemAccess;

namespace Observation.Handlers.Apps;

/// <summary>
/// Возможности Windows (Windows Capabilities) — отдельный от Optional Features
/// механизм, тот же, что OpenSSH Server/Client в плане: Add-WindowsCapability /
/// Remove-WindowsCapability, проверка через Get-WindowsCapability.
/// </summary>
public static class WindowsCapabilityHandler
{
    public static PowerShellToggleHandler Create(ICommandRunner runner, string capabilityName) => new(
        runner,
        onScript: $"Add-WindowsCapability -Online -Name '{capabilityName}' -ErrorAction Stop | Out-Null",
        offScript: $"Remove-WindowsCapability -Online -Name '{capabilityName}' -ErrorAction Stop | Out-Null",
        verifyScript: $"(Get-WindowsCapability -Online -Name '{capabilityName}').State -eq 'Installed'");
}
