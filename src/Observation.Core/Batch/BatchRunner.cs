using Observation.Core.Engine;
using Observation.Core.Journal;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;

namespace Observation.Core.Batch;

public sealed record QueuedTweak(TweakDefinition Tweak, bool DesiredOn);

public sealed record BatchRunResult(Guid BatchId, IReadOnlyList<TweakOutcome> Outcomes)
{
    public bool AllSucceeded => Outcomes.All(o => o.Success);
}

/// <summary>
/// Оркестрация накопительного пакета — BatchId, заголовок в журнал до первого применения,
/// продолжаем при ошибке (отчёт в конце), откат всего пакета в обратном порядке.
/// См. «Механизм безопасности и отката» и «Восстановление после сбоя приложения» в плане.
/// </summary>
public sealed class BatchRunner
{
    private readonly TweakEngine _engine;
    private readonly IJournalStore _journal;

    public BatchRunner(TweakEngine engine, IJournalStore journal)
    {
        _engine = engine;
        _journal = journal;
    }

    public async Task<BatchRunResult> RunAsync(IReadOnlyList<QueuedTweak> items, SystemContext systemContext, CancellationToken cancellationToken = default)
    {
        var batchId = Guid.NewGuid();
        _journal.BeginBatch(batchId, items.Count);

        var outcomes = new List<TweakOutcome>();
        foreach (var item in items)
        {
            var outcome = await _engine.ApplyAsync(item.Tweak, item.DesiredOn, systemContext, cancellationToken);
            outcomes.Add(outcome);

            _journal.AppendEntry(new JournalEntry
            {
                BatchId = batchId,
                TweakId = outcome.TweakId,
                Timestamp = DateTimeOffset.UtcNow,
                Success = outcome.Success,
                Message = outcome.Message,
                PreviousValueJson = outcome.PreviousValueJson,
                NewValueJson = outcome.NewValueJson
            });

            if (outcome.Success && item.Tweak.Revert.Type is RevertType.RestorePrevious or RevertType.AdditiveTagged)
            {
                _journal.UpsertActiveTweak(new ActiveTweakState
                {
                    TweakId = item.Tweak.Id,
                    DesiredOn = item.DesiredOn,
                    ExpectedValueJson = outcome.NewValueJson,
                    LastAppliedAt = DateTimeOffset.UtcNow
                });
            }
        }

        _journal.CompleteBatch(batchId);
        return new BatchRunResult(batchId, outcomes);
    }

    /// <summary>Откат всего пакета — проходим записи в обратном порядке, откатывая каждую по её типу.</summary>
    public async Task<BatchRunResult> RevertBatchAsync(Guid batchId, IReadOnlyDictionary<string, TweakDefinition> tweaksById, CancellationToken cancellationToken = default)
    {
        var entries = _journal.GetEntriesForBatch(batchId)
            .Where(e => e.Success)
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        var outcomes = new List<TweakOutcome>();
        foreach (var entry in entries)
        {
            if (!tweaksById.TryGetValue(entry.TweakId, out var tweak))
            {
                outcomes.Add(TweakOutcome.Failed(entry.TweakId, "Твик не найден в реестре — откат невозможен"));
                continue;
            }

            var outcome = await _engine.RevertAsync(tweak, entry.PreviousValueJson, cancellationToken);
            outcomes.Add(outcome);
            if (outcome.Success)
                _journal.RemoveActiveTweak(tweak.Id, entry.UserSid);
        }

        return new BatchRunResult(batchId, outcomes);
    }
}
