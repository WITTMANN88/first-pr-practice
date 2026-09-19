using Microsoft.Win32;
using Observation.Core.Handlers;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;

namespace Observation.Core.Engine;

/// <summary>
/// Применение/проверка/откат одного твика — ядро движка. Оркестрация пакета (BatchId,
/// журнал, порядок) — в Observation.Core.Batch.BatchRunner, который использует этот класс.
/// </summary>
public sealed class TweakEngine
{
    private readonly IRegistryAccessor _registry;
    private readonly IReadOnlyDictionary<string, ITweakHandler> _handlers;

    public TweakEngine(IRegistryAccessor registry, IReadOnlyDictionary<string, ITweakHandler> handlers)
    {
        _registry = registry;
        _handlers = handlers;
    }

    public async Task<TweakOutcome> ApplyAsync(TweakDefinition tweak, bool desiredOn, SystemContext systemContext, CancellationToken cancellationToken = default)
    {
        var applicability = tweak.Applicability.Evaluate(systemContext);
        if (!applicability.IsApplicable)
            return TweakOutcome.NotApplicable(tweak.Id, applicability.Reason!);

        return tweak.Apply switch
        {
            RegistryApplySpec reg => ApplyRegistry(tweak, reg, desiredOn),
            HandlerApplySpec handlerSpec => await ApplyHandlerAsync(tweak, handlerSpec, desiredOn, cancellationToken),
            _ => throw new NotSupportedException($"Неизвестный тип apply: {tweak.Apply.GetType().Name}")
        };
    }

    public async Task<bool> VerifyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        return tweak.Verify switch
        {
            RegistryReadVerifySpec verify => VerifyRegistryRead(tweak, verify, desiredOn),
            ServiceQueryVerifySpec serviceVerify => VerifyServiceQuery(serviceVerify),
            HandlerVerifySpec handlerVerify => await ResolveHandler(handlerVerify.HandlerId).VerifyAsync(tweak, desiredOn, cancellationToken),
            _ => throw new NotSupportedException($"Неизвестный тип verify: {tweak.Verify.GetType().Name}")
        };
    }

    public async Task<TweakOutcome> RevertAsync(TweakDefinition tweak, string? previousValueJson, CancellationToken cancellationToken = default)
    {
        if (tweak.Revert.Type is RevertType.BestEffort or RevertType.Unavailable or RevertType.ManualGuided)
        {
            return TweakOutcome.Failed(tweak.Id,
                $"Твик с типом отката '{tweak.Revert.Type}' не поддерживает автоматический откат — см. «Механизм отката — по типам»");
        }

        return tweak.Apply switch
        {
            RegistryApplySpec reg => RevertRegistry(tweak, reg, previousValueJson),
            HandlerApplySpec handlerSpec => await RevertHandlerAsync(tweak, handlerSpec, previousValueJson, cancellationToken),
            _ => throw new NotSupportedException($"Неизвестный тип apply: {tweak.Apply.GetType().Name}")
        };
    }

    private async Task<TweakOutcome> RevertHandlerAsync(TweakDefinition tweak, HandlerApplySpec spec, string? previousValueJson, CancellationToken cancellationToken)
    {
        var result = await ResolveHandler(spec.HandlerId).RevertAsync(tweak, previousValueJson, cancellationToken);
        return result.Success
            ? TweakOutcome.Succeeded(tweak.Id, previousValueJson, null, result.Message)
            : TweakOutcome.Failed(tweak.Id, result.Message ?? "Обработчик сообщил о неудаче отката");
    }

    private TweakOutcome ApplyRegistry(TweakDefinition tweak, RegistryApplySpec reg, bool desiredOn)
    {
        var needsCapture = tweak.Revert.Type is RevertType.RestorePrevious or RevertType.AdditiveTagged;
        var hadPreviousValue = false;
        object? previousData = null;

        if (needsCapture)
        {
            try
            {
                hadPreviousValue = _registry.TryReadValue(reg.Hive, reg.Path, reg.Value, out previousData, out _);
            }
            catch (Exception ex)
            {
                return TweakOutcome.Failed(tweak.Id, $"Не удалось прочитать текущее значение — твик не применён: {ex.Message}");
            }
        }

        object desiredData;
        try
        {
            desiredData = RegistryValueCodec.ToClrValue(desiredOn ? reg.OnData : reg.OffData, reg.ValueType);
        }
        catch (Exception ex)
        {
            return TweakOutcome.Failed(tweak.Id, $"Некорректное значение в схеме твика: {ex.Message}");
        }

        try
        {
            _registry.WriteValue(reg.Hive, reg.Path, reg.Value, desiredData, reg.ValueType);
        }
        catch (Exception ex)
        {
            return TweakOutcome.Failed(tweak.Id, $"Не удалось записать значение реестра: {ex.Message}");
        }

        var previousJson = needsCapture ? RegistryValueCodec.SerializeCapturedState(hadPreviousValue, previousData) : null;
        var newJson = RegistryValueCodec.SerializeCapturedState(true, desiredData);

        var verified = VerifyRegistryRead(tweak, (RegistryReadVerifySpec)tweak.Verify, desiredOn);
        return verified
            ? TweakOutcome.Succeeded(tweak.Id, previousJson, newJson)
            : TweakOutcome.Failed(tweak.Id, "Значение записано, но проверка после записи не подтвердила ожидаемый результат", previousJson, newJson);
    }

    private TweakOutcome RevertRegistry(TweakDefinition tweak, RegistryApplySpec reg, string? previousValueJson)
    {
        if (previousValueJson is null)
            return TweakOutcome.Failed(tweak.Id, "Нет сохранённого состояния «до» — откат невозможен");

        try
        {
            var captured = RegistryValueCodec.DeserializeCapturedState(previousValueJson);
            if (!captured.Existed)
            {
                _registry.DeleteValue(reg.Hive, reg.Path, reg.Value);
            }
            else
            {
                var restored = RegistryValueCodec.ToClrValue(captured.Value, reg.ValueType);
                _registry.WriteValue(reg.Hive, reg.Path, reg.Value, restored, reg.ValueType);
            }

            return TweakOutcome.Succeeded(tweak.Id, previousValueJson, null, "Откачено");
        }
        catch (Exception ex)
        {
            return TweakOutcome.Failed(tweak.Id, $"Не удалось откатить значение реестра: {ex.Message}");
        }
    }

    private bool VerifyRegistryRead(TweakDefinition tweak, RegistryReadVerifySpec verify, bool desiredOn)
    {
        if (!verify.MatchesApply)
            throw new NotSupportedException("registry-read verify с matchesApply=false не описан в плане — схема твика должна использовать matchesApply=true");

        if (tweak.Apply is not RegistryApplySpec reg)
            throw new NotSupportedException("registry-read verify с matchesApply=true применим только к registry apply");

        if (!_registry.TryReadValue(reg.Hive, reg.Path, reg.Value, out var actual, out _))
            return false;

        var expected = RegistryValueCodec.ToClrValue(desiredOn ? reg.OnData : reg.OffData, reg.ValueType);
        return RegistryValueCodec.ValuesEqual(actual, expected);
    }

    private static bool VerifyServiceQuery(ServiceQueryVerifySpec verify)
    {
        using var controller = new System.ServiceProcess.ServiceController(verify.ServiceName);
        return string.Equals(controller.StartType.ToString(), verify.ExpectedStartType, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TweakOutcome> ApplyHandlerAsync(TweakDefinition tweak, HandlerApplySpec spec, bool desiredOn, CancellationToken cancellationToken)
    {
        var handler = ResolveHandler(spec.HandlerId);
        var result = await handler.ApplyAsync(tweak, desiredOn, cancellationToken);
        if (!result.Success)
            return TweakOutcome.Failed(tweak.Id, result.Message ?? "Обработчик сообщил о неудаче");

        var verified = await VerifyAsync(tweak, desiredOn, cancellationToken);
        return verified
            ? TweakOutcome.Succeeded(tweak.Id, result.CapturedState, null, result.Message)
            : TweakOutcome.Failed(tweak.Id, "Обработчик отработал, но проверка не подтвердила ожидаемый результат", result.CapturedState);
    }

    private ITweakHandler ResolveHandler(string handlerId) =>
        _handlers.TryGetValue(handlerId, out var handler)
            ? handler
            : throw new InvalidOperationException($"Обработчик '{handlerId}' не зарегистрирован в Observation.Handlers");
}
