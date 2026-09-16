using System.Diagnostics;
using System.Text.Json.Nodes;
using Observation.Core.Handlers;
using Observation.Core.Tweaks;

namespace Observation.Handlers.Discord;

/// <summary>
/// Подтверждено экспериментально (diff-тест на реальной машине): ключ enableHardwareAcceleration
/// (булево) в %AppData%\discord\settings.json. Переключение User Settings → Advanced → Hardware
/// Acceleration в Discord напрямую и изолированно меняет только это поле — остальные ключи файла
/// (WINDOW_BOUNDS, IS_MAXIMIZED, BACKGROUND_COLOR, audioSubsystem и т.п.) не задеваются, поэтому
/// правим значение через JsonNode (сохраняет прочие ключи как есть), а не через строгую POCO-модель.
/// Discord должен быть полностью закрыт (через трей, не просто окно) на момент записи.
/// </summary>
public sealed class DiscordHardwareAccelerationHandler : ITweakHandler
{
    private const string KeyName = "enableHardwareAcceleration";
    private readonly string _settingsPath;

    public DiscordHardwareAccelerationHandler(string? settingsPathOverride = null) =>
        _settingsPath = settingsPathOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord", "settings.json");

    public Task<HandlerApplyResult> ApplyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        if (IsDiscordRunning())
            return Task.FromResult(new HandlerApplyResult(false, "Discord запущен — полностью закройте его (через трей), прежде чем применять этот твик"));

        if (!File.Exists(_settingsPath))
            return Task.FromResult(new HandlerApplyResult(false, $"Не найден файл настроек Discord: {_settingsPath}"));

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!.AsObject();
            var previousState = root.TryGetPropertyValue(KeyName, out var previousNode) && previousNode is not null
                ? previousNode.GetValue<bool>().ToString()
                : null;

            root[KeyName] = desiredOn;
            File.WriteAllText(_settingsPath, root.ToJsonString());

            return Task.FromResult(new HandlerApplyResult(true, null, previousState));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HandlerApplyResult(false, $"Не удалось изменить settings.json: {ex.Message}"));
        }
    }

    public Task<bool> VerifyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
            return Task.FromResult(false);

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!.AsObject();
            return Task.FromResult(root.TryGetPropertyValue(KeyName, out var node) && node is not null && node.GetValue<bool>() == desiredOn);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    public Task<HandlerApplyResult> RevertAsync(TweakDefinition tweak, string? capturedState, CancellationToken cancellationToken = default)
    {
        if (IsDiscordRunning())
            return Task.FromResult(new HandlerApplyResult(false, "Discord запущен — полностью закройте его (через трей), прежде чем откатывать этот твик"));

        if (!File.Exists(_settingsPath))
            return Task.FromResult(new HandlerApplyResult(false, $"Не найден файл настроек Discord: {_settingsPath}"));

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_settingsPath))!.AsObject();
            if (capturedState is null)
                root.Remove(KeyName);
            else
                root[KeyName] = bool.Parse(capturedState);

            File.WriteAllText(_settingsPath, root.ToJsonString());
            return Task.FromResult(new HandlerApplyResult(true, "Откачено"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HandlerApplyResult(false, $"Не удалось откатить settings.json: {ex.Message}"));
        }
    }

    private static bool IsDiscordRunning() => Process.GetProcessesByName("Discord").Length > 0;
}
