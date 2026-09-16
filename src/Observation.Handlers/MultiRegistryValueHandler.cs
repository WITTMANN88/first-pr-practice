using System.Text.Json;
using Microsoft.Win32;
using Observation.Core.Handlers;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;

namespace Observation.Handlers;

/// <summary>
/// Общий обработчик для твиков вида «включить/выключить N реестровых DWORD-политик
/// одним тумблером» (например «Деблоат Brave/Edge» — несколько политик под одним
/// переключателем интерфейса). Invert=true — для политик вида "...Enabled", где 0
/// означает выключенную функцию (противоположная полярность от "...Disabled"=1).
/// CapturedState — JSON-массив предыдущих состояний всех политик, для restore-previous.
/// </summary>
public sealed class MultiRegistryValueHandler : ITweakHandler
{
    public sealed record PolicyValue(RegistryHive Hive, string Path, string ValueName, bool Invert = false);

    private readonly IRegistryAccessor _registry;
    private readonly IReadOnlyList<PolicyValue> _policies;

    public MultiRegistryValueHandler(IRegistryAccessor registry, IReadOnlyList<PolicyValue> policies)
    {
        _registry = registry;
        _policies = policies;
    }

    public Task<HandlerApplyResult> ApplyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        var captured = new List<CapturedPolicy>();
        foreach (var policy in _policies)
        {
            bool existed;
            object? previous;
            try
            {
                existed = _registry.TryReadValue(policy.Hive, policy.Path, policy.ValueName, out previous, out _);
            }
            catch (Exception ex)
            {
                return Task.FromResult(new HandlerApplyResult(false,
                    $"Не удалось прочитать текущее значение «{policy.ValueName}» — твик не применён: {ex.Message}"));
            }

            captured.Add(new CapturedPolicy(policy.ValueName, existed, existed ? Convert.ToInt32(previous) : 0));
        }

        try
        {
            foreach (var policy in _policies)
                _registry.WriteValue(policy.Hive, policy.Path, policy.ValueName, EffectiveValue(desiredOn, policy), RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HandlerApplyResult(false, $"Не удалось записать значение реестра: {ex.Message}"));
        }

        return Task.FromResult(new HandlerApplyResult(true, null, JsonSerializer.Serialize(captured)));
    }

    public Task<bool> VerifyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        foreach (var policy in _policies)
        {
            if (!_registry.TryReadValue(policy.Hive, policy.Path, policy.ValueName, out var actual, out _)
                || Convert.ToInt32(actual) != EffectiveValue(desiredOn, policy))
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    public Task<HandlerApplyResult> RevertAsync(TweakDefinition tweak, string? capturedState, CancellationToken cancellationToken = default)
    {
        if (capturedState is null)
            return Task.FromResult(new HandlerApplyResult(false, "Нет сохранённого состояния «до» — откат невозможен"));

        List<CapturedPolicy>? captured;
        try
        {
            captured = JsonSerializer.Deserialize<List<CapturedPolicy>>(capturedState);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HandlerApplyResult(false, $"Не удалось прочитать сохранённое состояние: {ex.Message}"));
        }

        if (captured is null)
            return Task.FromResult(new HandlerApplyResult(false, "Сохранённое состояние пусто"));

        try
        {
            foreach (var policy in _policies)
            {
                var saved = captured.FirstOrDefault(c => c.ValueName == policy.ValueName);
                if (saved is null)
                    continue;

                if (!saved.Existed)
                    _registry.DeleteValue(policy.Hive, policy.Path, policy.ValueName);
                else
                    _registry.WriteValue(policy.Hive, policy.Path, policy.ValueName, saved.Value, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HandlerApplyResult(false, $"Не удалось откатить значения реестра: {ex.Message}"));
        }

        return Task.FromResult(new HandlerApplyResult(true, "Откачено"));
    }

    private static int EffectiveValue(bool desiredOn, PolicyValue policy) => (desiredOn ^ policy.Invert) ? 1 : 0;

    private sealed record CapturedPolicy(string ValueName, bool Existed, int Value);
}
