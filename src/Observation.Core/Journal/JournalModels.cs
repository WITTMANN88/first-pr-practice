namespace Observation.Core.Journal;

/// <summary>Статус пакета применения — см. «Восстановление после сбоя приложения» в плане.</summary>
public enum BatchStatus
{
    InProgress,
    Completed,
    AcknowledgedIncomplete
}

/// <summary>Заголовок пакета — пишется в журнал до применения первого твика, до того, как что-то реально меняется.</summary>
public sealed class BatchHeader
{
    public required Guid BatchId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required int TweaksPlanned { get; init; }
    public required BatchStatus Status { get; init; }
    public int TweaksApplied { get; init; }
}

/// <summary>Одна запись в append-only журнале — результат применения/отката одного твика в рамках пакета.</summary>
public sealed class JournalEntry
{
    public required Guid BatchId { get; init; }
    public required string TweakId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? PreviousValueJson { get; init; }
    public string? NewValueJson { get; init; }

    /// <summary>Для «применения ко всем профилям» — SID пользователя, если твик применялся не для текущего.</summary>
    public string? UserSid { get; init; }
}

/// <summary>
/// Текущий срез «активных твиков» (не история) — на каждый твик с восстанавливаемым
/// откатом одна запись: «включён, ожидаемое значение такое-то». Используется кнопкой
/// «Проверить состояние» на Главной для повторного verify.
/// </summary>
public sealed class ActiveTweakState
{
    public required string TweakId { get; init; }
    public string? UserSid { get; init; }
    public required bool DesiredOn { get; init; }
    public string? ExpectedValueJson { get; init; }
    public required DateTimeOffset LastAppliedAt { get; init; }
}
