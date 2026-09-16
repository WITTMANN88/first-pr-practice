using Observation.Core.Handlers;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;

namespace Observation.Handlers;

/// <summary>
/// Общий обработчик для твиков вида «выполнить PowerShell-команду на вкл/выкл, проверить
/// отдельной командой, печатающей True/False» (Defender, firewall, службы и т.п. — см.
/// «Исполнение и системный доступ» в плане). "Выключить" — это отдельная команда (полноценный
/// forward-apply, как offData у реестровых твиков), а не восстановление точного состояния «до»,
/// поэтому такие твики честно помечаются revert.type=best-effort в JSON-реестре — TweakEngine
/// сам отказывает в откате через журнал для best-effort, до вызова RevertAsync здесь.
/// </summary>
public sealed class PowerShellToggleHandler : ITweakHandler
{
    private readonly ICommandRunner _runner;
    private readonly string _onScript;
    private readonly string _offScript;
    private readonly string _verifyScript;

    public PowerShellToggleHandler(ICommandRunner runner, string onScript, string offScript, string verifyScript)
    {
        _runner = runner;
        _onScript = onScript;
        _offScript = offScript;
        _verifyScript = verifyScript;
    }

    public async Task<HandlerApplyResult> ApplyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        var result = await RunPowerShellAsync(desiredOn ? _onScript : _offScript, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? new HandlerApplyResult(true, null, null)
            : new HandlerApplyResult(false, $"Команда завершилась с ошибкой: {FirstNonEmptyLine(result.StandardError, result.StandardOutput)}");
    }

    public async Task<bool> VerifyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        var result = await RunPowerShellAsync(_verifyScript, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            return false;

        var actualOn = result.StandardOutput.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
        return actualOn == desiredOn;
    }

    public Task<HandlerApplyResult> RevertAsync(TweakDefinition tweak, string? capturedState, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HandlerApplyResult(false,
            "Командный твик — best-effort, автоматический откат через журнал не поддерживается " +
            "(см. «Механизм отката — по типам»); переключите тумблер обратно и примените, чтобы получить противоположный эффект"));

    private Task<CommandResult> RunPowerShellAsync(string script, CancellationToken cancellationToken) =>
        _runner.RunAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, cancellationToken);

    private static string FirstNonEmptyLine(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var trimmed = candidate.Trim();
            if (trimmed.Length > 0)
                return trimmed.Split('\n')[0];
        }

        return "неизвестная ошибка";
    }
}
