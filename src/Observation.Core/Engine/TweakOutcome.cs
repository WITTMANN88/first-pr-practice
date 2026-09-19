namespace Observation.Core.Engine;

/// <summary>Результат применения/отката одного твика — то, что попадает в запись журнала.</summary>
public sealed class TweakOutcome
{
    public required string TweakId { get; init; }
    public required bool Success { get; init; }
    public bool Skipped { get; init; }
    public string? Message { get; init; }
    public string? PreviousValueJson { get; init; }
    public string? NewValueJson { get; init; }

    public static TweakOutcome NotApplicable(string tweakId, string reason) =>
        new() { TweakId = tweakId, Success = false, Skipped = true, Message = reason };

    public static TweakOutcome Failed(string tweakId, string message, string? previousJson = null, string? newJson = null) =>
        new() { TweakId = tweakId, Success = false, Message = message, PreviousValueJson = previousJson, NewValueJson = newJson };

    public static TweakOutcome Succeeded(string tweakId, string? previousJson, string? newJson, string? message = null) =>
        new() { TweakId = tweakId, Success = true, Message = message, PreviousValueJson = previousJson, NewValueJson = newJson };
}
