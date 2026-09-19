using Observation.Core.Tweaks;

namespace Observation.Core.Handlers;

/// <summary>
/// Реализуется в Observation.Handlers для твиков, не описываемых одним значением реестра
/// (OneDrive, деблоат Edge/Brave, мастер DDU, скрипты) — см. handlerId в HandlerApplySpec.
/// CapturedState — непрозрачная строка (обычно JSON), которую сам обработчик определяет
/// и получает обратно при revert; так Core не привязан к форме состояния конкретного твика.
/// </summary>
public interface ITweakHandler
{
    Task<HandlerApplyResult> ApplyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default);

    /// <summary>Для RevertType.Unavailable/ManualGuided вызывающая сторона не должна вызывать Revert — обработчику незачем его поддерживать.</summary>
    Task<HandlerApplyResult> RevertAsync(TweakDefinition tweak, string? capturedState, CancellationToken cancellationToken = default);
}

public sealed record HandlerApplyResult(bool Success, string? Message, string? CapturedState = null);
