using System.IO;
using Observation.Core.Journal;
using Xunit;

namespace Observation.Tests;

public class JsonLinesJournalStoreTests : IDisposable
{
    private readonly string _dataFolder = Path.Combine(Path.GetTempPath(), "ObservationTests_" + Guid.NewGuid());

    private JsonLinesJournalStore CreateStore() => new(_dataFolder);

    [Fact]
    public void GetLatestEntryForTweak_ReturnsMostRecentSuccessfulEntry()
    {
        var store = CreateStore();
        var batchA = Guid.NewGuid();
        var batchB = Guid.NewGuid();

        store.BeginBatch(batchA, 1);
        store.AppendEntry(new JournalEntry { BatchId = batchA, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10), Success = true, PreviousValueJson = "old" });
        store.CompleteBatch(batchA);

        store.BeginBatch(batchB, 1);
        store.AppendEntry(new JournalEntry { BatchId = batchB, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow, Success = true, PreviousValueJson = "newer" });
        store.CompleteBatch(batchB);

        var latest = store.GetLatestEntryForTweak("perf.gamemode");

        Assert.NotNull(latest);
        Assert.Equal("newer", latest!.PreviousValueJson);
    }

    [Fact]
    public void GetLatestEntryForTweak_IgnoresFailedEntries()
    {
        var store = CreateStore();
        var batchId = Guid.NewGuid();

        store.BeginBatch(batchId, 1);
        store.AppendEntry(new JournalEntry { BatchId = batchId, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow, Success = false, PreviousValueJson = "should-be-ignored" });
        store.CompleteBatch(batchId);

        Assert.Null(store.GetLatestEntryForTweak("perf.gamemode"));
    }

    [Fact]
    public void GetRecentEntries_ReturnsNewestFirst_AcrossBatches()
    {
        var store = CreateStore();
        var batchA = Guid.NewGuid();
        var batchB = Guid.NewGuid();

        store.BeginBatch(batchA, 1);
        store.AppendEntry(new JournalEntry { BatchId = batchA, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10), Success = true });
        store.CompleteBatch(batchA);

        store.BeginBatch(batchB, 1);
        store.AppendEntry(new JournalEntry { BatchId = batchB, TweakId = "privacy.diagtrack", Timestamp = DateTimeOffset.UtcNow, Success = false, Message = "ошибка" });
        store.CompleteBatch(batchB);

        var recent = store.GetRecentEntries(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal("privacy.diagtrack", recent[0].TweakId);
        Assert.Equal("perf.gamemode", recent[1].TweakId);
    }

    [Fact]
    public void GetRecentEntries_RespectsCountLimit()
    {
        var store = CreateStore();
        var batchId = Guid.NewGuid();

        store.BeginBatch(batchId, 3);
        for (var i = 0; i < 3; i++)
            store.AppendEntry(new JournalEntry { BatchId = batchId, TweakId = $"tweak{i}", Timestamp = DateTimeOffset.UtcNow.AddSeconds(i), Success = true });
        store.CompleteBatch(batchId);

        Assert.Equal(2, store.GetRecentEntries(2).Count);
    }

    [Fact]
    public void FindIncompleteBatch_ReturnsNull_AfterCompleteBatch()
    {
        var store = CreateStore();
        var batchId = Guid.NewGuid();

        store.BeginBatch(batchId, tweaksPlanned: 3);
        store.AppendEntry(new JournalEntry { BatchId = batchId, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow, Success = true });
        store.CompleteBatch(batchId);

        Assert.Null(store.FindIncompleteBatch());
    }

    [Fact]
    public void FindIncompleteBatch_ReturnsHeader_WhenBatchNeverCompleted()
    {
        var store = CreateStore();
        var batchId = Guid.NewGuid();

        store.BeginBatch(batchId, tweaksPlanned: 5);
        store.AppendEntry(new JournalEntry { BatchId = batchId, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow, Success = true });
        store.AppendEntry(new JournalEntry { BatchId = batchId, TweakId = "apps.discord.hwaccel", Timestamp = DateTimeOffset.UtcNow, Success = true });

        var incomplete = store.FindIncompleteBatch();

        Assert.NotNull(incomplete);
        Assert.Equal(batchId, incomplete!.BatchId);
        Assert.Equal(5, incomplete.TweaksPlanned);
        Assert.Equal(2, incomplete.TweaksApplied);
        Assert.Equal(BatchStatus.InProgress, incomplete.Status);
    }

    [Fact]
    public void FindIncompleteBatch_ReturnsNull_AfterAcknowledged()
    {
        var store = CreateStore();
        var batchId = Guid.NewGuid();

        store.BeginBatch(batchId, tweaksPlanned: 1);
        store.AcknowledgeIncomplete(batchId);

        Assert.Null(store.FindIncompleteBatch());
    }

    [Fact]
    public void GetEntriesForBatch_ReturnsOnlyEntriesForThatBatch()
    {
        var store = CreateStore();
        var batchA = Guid.NewGuid();
        var batchB = Guid.NewGuid();

        store.BeginBatch(batchA, 1);
        store.AppendEntry(new JournalEntry { BatchId = batchA, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow, Success = true });
        store.CompleteBatch(batchA);

        store.BeginBatch(batchB, 1);
        store.AppendEntry(new JournalEntry { BatchId = batchB, TweakId = "apps.discord.hwaccel", Timestamp = DateTimeOffset.UtcNow, Success = true });
        store.CompleteBatch(batchB);

        var entriesA = store.GetEntriesForBatch(batchA);

        Assert.Single(entriesA);
        Assert.Equal("perf.gamemode", entriesA[0].TweakId);
    }

    [Fact]
    public void ActiveTweaks_UpsertGetRemove_RoundTrips()
    {
        var store = CreateStore();
        var state = new ActiveTweakState { TweakId = "perf.gamemode", DesiredOn = true, ExpectedValueJson = "1", LastAppliedAt = DateTimeOffset.UtcNow };

        store.UpsertActiveTweak(state);
        Assert.Single(store.GetActiveTweaks());

        store.RemoveActiveTweak("perf.gamemode", userSid: null);
        Assert.Empty(store.GetActiveTweaks());
    }

    [Fact]
    public void Rotation_ArchivesJournal_WhenEntryLimitExceeded()
    {
        var store = CreateStore();

        // 2000-entry limit is checked after each completed batch — 700 small batches
        // (begin+entry+complete = 3 lines each) comfortably exceed it.
        for (var i = 0; i < 700; i++)
        {
            var batchId = Guid.NewGuid();
            store.BeginBatch(batchId, 1);
            store.AppendEntry(new JournalEntry { BatchId = batchId, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow, Success = true });
            store.CompleteBatch(batchId);
        }

        var archiveFolder = Path.Combine(_dataFolder, "journal_archive");
        Assert.True(Directory.Exists(archiveFolder));
        Assert.NotEmpty(Directory.GetFiles(archiveFolder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
            Directory.Delete(_dataFolder, recursive: true);
    }
}
