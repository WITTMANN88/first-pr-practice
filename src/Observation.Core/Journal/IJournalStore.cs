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

    IReadOnlyList<ActiveTweakState> GetActiveTweaks();
    void UpsertActiveTweak(ActiveTweakState state);
    void RemoveActiveTweak(string tweakId, string? userSid);
}
