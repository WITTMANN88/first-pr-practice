using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Observation.Core.Batch;
using Observation.Core.Engine;
using Observation.Core.Handlers;
using Observation.Core.Journal;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class BatchRunnerTests : IDisposable
{
    private readonly string _dataFolder = Path.Combine(Path.GetTempPath(), "ObservationTests_" + Guid.NewGuid());
    private static readonly SystemContext DefaultContext = new(22631, "Professional", "23H2", "AMD Ryzen 7 7800X3D");

    private static TweakDefinition GameModeTweak() => new()
    {
        Id = "perf.gamemode",
        Tab = "performance",
        Group = "Питание и режимы",
        Name = new LocalizedText { Ru = "Режим игры", En = "Game Mode" },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Apply = new RegistryApplySpec
        {
            Hive = RegistryHive.CurrentUser,
            Path = @"Software\Microsoft\GameBar",
            Value = "AllowAutoGameMode",
            ValueType = RegistryValueKind.DWord,
            OnData = JsonSerializer.SerializeToElement(1),
            OffData = JsonSerializer.SerializeToElement(0)
        },
        Verify = new RegistryReadVerifySpec { MatchesApply = true },
        Revert = new RevertSpec { Type = RevertType.RestorePrevious }
    };

    [Fact]
    public async Task RunAsync_CompletesBatch_AndRegistersActiveTweak()
    {
        var registry = new FakeRegistryAccessor();
        var engine = new TweakEngine(registry, new Dictionary<string, ITweakHandler>());
        var journal = new JsonLinesJournalStore(_dataFolder);
        var runner = new BatchRunner(engine, journal);
        var tweak = GameModeTweak();

        var result = await runner.RunAsync(new[] { new QueuedTweak(tweak, true) }, DefaultContext);

        Assert.True(result.AllSucceeded);
        Assert.Null(journal.FindIncompleteBatch());
        Assert.Single(journal.GetActiveTweaks());
    }

    [Fact]
    public async Task RunAsync_ThenRevertBatch_RestoresPreviousRegistryValue()
    {
        var registry = new FakeRegistryAccessor();
        registry.Seed(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 0, RegistryValueKind.DWord);
        var engine = new TweakEngine(registry, new Dictionary<string, ITweakHandler>());
        var journal = new JsonLinesJournalStore(_dataFolder);
        var runner = new BatchRunner(engine, journal);
        var tweak = GameModeTweak();

        var runResult = await runner.RunAsync(new[] { new QueuedTweak(tweak, true) }, DefaultContext);
        var revertResult = await runner.RevertBatchAsync(runResult.BatchId, new Dictionary<string, TweakDefinition> { [tweak.Id] = tweak });

        Assert.True(revertResult.AllSucceeded);
        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", out var data, out _));
        Assert.Equal(0, data);
    }

    [Fact]
    public void CrashRecovery_DetectsInProgressBatch_WrittenBeforeCrash()
    {
        var journal = new JsonLinesJournalStore(_dataFolder);
        var batchId = Guid.NewGuid();

        // Имитация краха: заголовок пакета написан, твик применён и записан,
        // но CompleteBatch никогда не вызывался (процесс "упал" посреди пакета).
        journal.BeginBatch(batchId, tweaksPlanned: 2);
        journal.AppendEntry(new JournalEntry { BatchId = batchId, TweakId = "perf.gamemode", Timestamp = DateTimeOffset.UtcNow, Success = true });

        var recovered = new JsonLinesJournalStore(_dataFolder).FindIncompleteBatch();

        Assert.NotNull(recovered);
        Assert.Equal(batchId, recovered!.BatchId);
        Assert.Equal(1, recovered.TweaksApplied);
        Assert.Equal(2, recovered.TweaksPlanned);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
            Directory.Delete(_dataFolder, recursive: true);
    }
}
