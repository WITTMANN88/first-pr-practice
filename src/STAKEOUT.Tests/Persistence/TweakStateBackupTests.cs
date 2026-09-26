using System.Text.Json;
using Stakeout.Models;
using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Persistence;

/// <summary>Rotating backups of tweak-state.json and automatic recovery from them.</summary>
public sealed class TweakStateBackupTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string StatePath => _dir.File("tweak-state.json");
    private string BackupDir => Path.Combine(_dir.Path, "backups");

    public void Dispose() => _dir.Dispose();

    private static TweakState State(string note) => new() { Notes = { ["n"] = note } };

    /// <summary>Tweak ids recorded in a state file.</summary>
    private static string[] IdsIn(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.GetProperty("tweaks").EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray();
    }

    [Fact]
    public void EverySuccessfulApply_WritesABackupIdenticalToTheMainFile()
    {
        var store = new TweakStateStore(StatePath);
        Assert.True(store.MarkApplied("mpo", State("a")));

        var backup = Assert.Single(store.GetBackups());
        Assert.Equal(File.ReadAllText(StatePath), File.ReadAllText(backup));
        Assert.StartsWith(BackupDir, backup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotation_KeepsOnlyTheThreeNewestStates()
    {
        var store = new TweakStateStore(StatePath);
        for (var i = 1; i <= 5; i++) store.MarkApplied($"t{i}", State($"{i}"));

        var backups = store.GetBackups(); // newest first
        Assert.Equal(TweakStateStore.DefaultBackupsToKeep, backups.Count);
        Assert.Equal(new[] { "t1", "t2", "t3", "t4", "t5" }, IdsIn(backups[0]));
        Assert.Equal(new[] { "t1", "t2", "t3", "t4" }, IdsIn(backups[1]));
        Assert.Equal(new[] { "t1", "t2", "t3" }, IdsIn(backups[2]));
        Assert.Equal(3, Directory.GetFiles(BackupDir).Length); // nothing else left behind (no .tmp)
    }

    [Fact]
    public void Revert_AlsoBacksUp_SoTheNewestBackupNeverResurrectsRevertedTweaks()
    {
        var store = new TweakStateStore(StatePath);
        store.MarkApplied("uac", State("x"));
        store.MarkApplied("mpo", State("y"));
        store.MarkReverted("uac");

        Assert.Equal(new[] { "mpo" }, IdsIn(store.GetBackups()[0]));
        Assert.Equal(File.ReadAllText(StatePath), File.ReadAllText(store.GetBackups()[0]));
    }

    [Fact]
    public void CustomRetention_IsHonoured()
    {
        var store = new TweakStateStore(StatePath, backupsToKeep: 5);
        for (var i = 0; i < 8; i++) store.MarkApplied($"t{i}", State("x"));
        Assert.Equal(5, store.GetBackups().Count);
    }

    [Fact]
    public void RetentionZero_DisablesBackups()
    {
        var store = new TweakStateStore(StatePath, backupsToKeep: 0);
        store.MarkApplied("mpo", State("x"));

        Assert.Empty(store.GetBackups());
        Assert.False(Directory.Exists(BackupDir));
    }

    [Fact]
    public void NegativeRetention_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TweakStateStore(StatePath, backupsToKeep: -1));
    }

    [Fact]
    public void RapidSaves_GetUniqueNamesInSaveOrder()
    {
        var frozen = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc); // every save at the same instant
        var store = new TweakStateStore(StatePath, 10, () => frozen);
        for (var i = 0; i < 10; i++) store.MarkApplied($"t{i:D2}", State("x"));

        var backups = store.GetBackups();
        Assert.Equal(10, backups.Distinct().Count());
        Assert.Equal(10, IdsIn(backups[0]).Length);   // newest = last save
        Assert.Single(IdsIn(backups[^1]));             // oldest = first save
    }

    [Fact]
    public void ClockMovingBackwards_DoesNotLetPruningDeleteTheNewestBackup()
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var store = new TweakStateStore(StatePath, 3, () => now);
        store.MarkApplied("a", State("x"));
        store.MarkApplied("b", State("x"));
        store.MarkApplied("c", State("x"));

        now = now.AddHours(-5);                    // e.g. NTP correction
        store.MarkApplied("d", State("x"));

        Assert.Equal(new[] { "a", "b", "c", "d" }, IdsIn(store.GetBackups()[0]));
    }

    [Fact]
    public void ClockMovingBackwardsBetweenRuns_StillOrdersNewestFirst()
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        new TweakStateStore(StatePath, 3, () => now).MarkApplied("a", State("x"));

        var later = new TweakStateStore(StatePath, 3, () => now.AddDays(-1)); // next launch, clock behind
        later.MarkApplied("b", State("x"));

        Assert.Equal(new[] { "a", "b" }, IdsIn(later.GetBackups()[0]));
    }

    [Fact]
    public void FailedMainSave_WritesNoBackup()
    {
        var blocker = _dir.File("blocker");
        File.WriteAllText(blocker, "x");                          // parent "dir" is a file
        var store = new TweakStateStore(Path.Combine(blocker, "tweak-state.json"));

        Assert.False(store.MarkApplied("mpo", State("x")));
        Assert.Empty(store.GetBackups());
    }

    [Fact]
    public void BackupFailure_DoesNotFailTheSave()
    {
        File.WriteAllText(BackupDir, "not a directory");          // backups/ cannot be created
        var store = new TweakStateStore(StatePath);

        Assert.True(store.MarkApplied("mpo", State("x")));
        Assert.True(new TweakStateStore(StatePath).IsApplied("mpo"));
    }

    [Fact]
    public void CorruptMainFile_IsQuarantined_AndRecoveredFromNewestBackup()
    {
        var store = new TweakStateStore(StatePath);
        store.MarkApplied("uac", State("x"));
        store.MarkApplied("mpo", State("y"));
        var newestBackup = store.GetBackups()[0];
        File.WriteAllText(StatePath, "{ truncated by a crash");

        var recovered = new TweakStateStore(StatePath);

        Assert.Equal(newestBackup, recovered.RecoveredFrom);
        Assert.True(recovered.IsApplied("uac"));
        Assert.True(recovered.IsApplied("mpo"));
        Assert.Equal("y", recovered.GetApplied("mpo")!.Notes["n"]);
        Assert.Single(Directory.GetFiles(_dir.Path, "tweak-state.json.corrupt-*"));  // evidence kept
        Assert.Equal(new[] { "mpo", "uac" }, IdsIn(StatePath));                       // main file valid again
        Assert.Null(new TweakStateStore(StatePath).RecoveredFrom);                    // no second recovery
    }

    [Fact]
    public void CorruptNewestBackup_FallsBackToTheNextValidOne()
    {
        var store = new TweakStateStore(StatePath);
        store.MarkApplied("a", State("x"));
        store.MarkApplied("b", State("x"));
        var backups = store.GetBackups();
        File.WriteAllText(backups[0], "garbage");
        File.Delete(StatePath);

        var recovered = new TweakStateStore(StatePath);

        Assert.Equal(backups[1], recovered.RecoveredFrom);
        Assert.Equal(new[] { "a" }, recovered.AppliedTweakIds());
    }

    [Fact]
    public void AllBackupsCorrupt_StartsEmptyInsteadOfCrashing()
    {
        var store = new TweakStateStore(StatePath);
        store.MarkApplied("a", State("x"));
        foreach (var b in store.GetBackups()) File.WriteAllText(b, "garbage");
        File.WriteAllText(StatePath, "garbage");

        var recovered = new TweakStateStore(StatePath);

        Assert.Null(recovered.RecoveredFrom);
        Assert.Empty(recovered.AppliedTweakIds());
    }

    [Fact]
    public void MissingMainFile_WithBackups_IsRecovered()
    {
        var store = new TweakStateStore(StatePath);
        store.MarkApplied("hags", State("x"));
        File.Delete(StatePath);

        var recovered = new TweakStateStore(StatePath);

        Assert.True(recovered.IsApplied("hags"));
        Assert.True(File.Exists(StatePath));
    }

    [Fact]
    public void Recovery_DoesNotEvictOlderBackups()
    {
        var store = new TweakStateStore(StatePath);
        for (var i = 0; i < 3; i++) store.MarkApplied($"t{i}", State("x"));
        var before = store.GetBackups();
        File.WriteAllText(StatePath, "garbage");

        var recovered = new TweakStateStore(StatePath);

        Assert.Equal(before, recovered.GetBackups());
    }

    [Fact]
    public void FirstRun_WithNothingOnDisk_IsNotARecovery()
    {
        var store = new TweakStateStore(StatePath);
        Assert.Null(store.RecoveredFrom);
        Assert.Empty(store.GetBackups());
    }

    [Fact]
    public void UnrelatedFilesInTheBackupFolder_AreIgnoredAndNeverDeleted()
    {
        Directory.CreateDirectory(BackupDir);
        var unrelated = new[] { "readme.txt", "tweak-state.notastamp.Z.json", "tweak-state.20260101T000000.0000000Z.json.tmp" }
            .Select(n => Path.Combine(BackupDir, n)).ToArray();
        foreach (var f in unrelated) File.WriteAllText(f, "keep me");

        var store = new TweakStateStore(StatePath);
        for (var i = 0; i < 6; i++) store.MarkApplied($"t{i}", State("x"));

        Assert.Equal(3, store.GetBackups().Count);
        Assert.All(unrelated, f => Assert.True(File.Exists(f), $"{f} was deleted"));
    }

    [Fact]
    public async Task ConcurrentApplies_KeepRetention_AndNewestBackupMatchesMain()
    {
        var store = new TweakStateStore(StatePath);
        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => store.MarkApplied($"t{i}", State("x")))));

        var backups = store.GetBackups();
        Assert.Equal(3, backups.Count);
        Assert.Equal(await File.ReadAllTextAsync(StatePath), await File.ReadAllTextAsync(backups[0]));
        Assert.Equal(40, IdsIn(backups[0]).Length);
    }

    [Fact]
    public void InMemoryStore_WorksWithoutAnyFile()
    {
        var store = TweakStateStore.CreateInMemory();
        Assert.True(store.MarkApplied("mpo", State("x")));
        Assert.True(store.IsApplied("mpo"));
        Assert.Empty(store.GetBackups());
        Assert.Equal("", store.FilePath);
        Assert.True(store.MarkReverted("mpo"));
        Assert.False(store.IsApplied("mpo"));
    }
}
