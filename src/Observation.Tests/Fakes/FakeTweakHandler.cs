using Observation.Core.Handlers;
using Observation.Core.Tweaks;

namespace Observation.Tests.Fakes;

/// <summary>Мок процедурного обработчика — имитирует твик вроде Discord hwaccel (флаг в памяти вместо settings.json).</summary>
public sealed class FakeTweakHandler : ITweakHandler
{
    public bool CurrentValue { get; set; }
    public bool FailApply { get; set; }

    public Task<HandlerApplyResult> ApplyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        if (FailApply)
            return Task.FromResult(new HandlerApplyResult(false, "Симулированный отказ обработчика"));

        var previous = CurrentValue;
        CurrentValue = desiredOn;
        return Task.FromResult(new HandlerApplyResult(true, null, previous.ToString()));
    }

    public Task<bool> VerifyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default) =>
        Task.FromResult(CurrentValue == desiredOn);

    public Task<HandlerApplyResult> RevertAsync(TweakDefinition tweak, string? capturedState, CancellationToken cancellationToken = default)
    {
        if (capturedState is null || !bool.TryParse(capturedState, out var previous))
            return Task.FromResult(new HandlerApplyResult(false, "Нет сохранённого состояния «до»"));

        CurrentValue = previous;
        return Task.FromResult(new HandlerApplyResult(true, "Откачено"));
    }
}
