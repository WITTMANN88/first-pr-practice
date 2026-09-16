namespace Observation.Core.Journal;

public interface IJournalStore
{
    void BeginBatch(Guid batchId, int tweaksPlanned);
    void AppendEntry(JournalEntry entry);
    void CompleteBatch(Guid batchId);
    void AcknowledgeIncomplete(Guid batchId);

    /// <summary>Пакет со статусом InProgress без последующего Completed/AcknowledgedIncomplete — признак сбоя (см. план).</summary>
    BatchHeader? FindIncompleteBatch();

    IReadOnlyList<JournalEntry> GetEntriesForBatch(Guid batchId);

    /// <summary>Последняя успешная запись по конкретному твику — источник PreviousValueJson для точечного отката вне пакета.</summary>
    JournalEntry? GetLatestEntryForTweak(string tweakId);

    IReadOnlyList<ActiveTweakState> GetActiveTweaks();
    void UpsertActiveTweak(ActiveTweakState state);
    void RemoveActiveTweak(string tweakId, string? userSid);
}
