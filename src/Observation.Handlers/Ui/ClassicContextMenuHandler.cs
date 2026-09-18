using Microsoft.Win32;
using Observation.Core.Handlers;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;

namespace Observation.Handlers.Ui;

/// <summary>
/// Классическое контекстное меню Win11 — reg add на пустое значение по умолчанию у
/// специального CLSID (см. «Вкладка 7 — Кастомизация интерфейса» в плане). Не описывается
/// MultiRegistryValueHandler: «включить» — это запись значения, а «выключить» — это удаление
/// всего раздела целиком (не значения), чего IRegistryAccessor.DeleteValue не умеет — отсюда
/// отдельный IRegistryAccessor.DeleteKey и свой обработчик, а не общий реестровый твик.
/// Explorer перезапускается сразу же (Stop-Process + Start-Process), чтобы эффект был виден
/// без ручного перезапуска/перезагрузки — заметный визуально мерцание панели задач/рабочего
/// стола на секунду, это ожидаемо, не сбой.
/// </summary>
public sealed class ClassicContextMenuHandler : ITweakHandler
{
    private const string ClsidPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string ClsidKeyPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    private readonly IRegistryAccessor _registry;
    private readonly ICommandRunner _runner;

    public ClassicContextMenuHandler(IRegistryAccessor registry, ICommandRunner runner)
    {
        _registry = registry;
        _runner = runner;
    }

    public async Task<HandlerApplyResult> ApplyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        try
        {
            if (desiredOn)
                _registry.WriteValue(RegistryHive.CurrentUser, ClsidPath, string.Empty, string.Empty, RegistryValueKind.String);
            else
                _registry.DeleteKey(RegistryHive.CurrentUser, ClsidKeyPath);
        }
        catch (Exception ex)
        {
            return new HandlerApplyResult(false, $"Не удалось изменить раздел реестра: {ex.Message}");
        }

        await RestartExplorerAsync(cancellationToken).ConfigureAwait(false);
        return new HandlerApplyResult(true, null);
    }

    public Task<bool> VerifyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        var exists = _registry.TryReadValue(RegistryHive.CurrentUser, ClsidPath, string.Empty, out _, out _);
        return Task.FromResult(desiredOn == exists);
    }

    public Task<HandlerApplyResult> RevertAsync(TweakDefinition tweak, string? capturedState, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HandlerApplyResult(false,
            "Best-effort твик — переключите тумблер обратно и примените, чтобы получить противоположный эффект"));

    private Task RestartExplorerAsync(CancellationToken cancellationToken) =>
        _runner.RunAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                "Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Process explorer.exe" },
            cancellationToken);
}
