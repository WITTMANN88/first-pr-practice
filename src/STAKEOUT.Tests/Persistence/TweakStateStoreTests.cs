using System.Globalization;
using System.Text.Json;
using Stakeout.Models;
using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Persistence;

public sealed class TweakStateStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string StatePath => _dir.File("tweak-state.json");

    public void Dispose() => _dir.Dispose();

    private static TweakState SampleState() => new()
    {
        Saved =
        {
            new SavedValue { Hive = "HKLM", SubKey = @"SOFTWARE\Microsoft\Windows\Dwm", Name = "OverlayTestMode",
                             Existed = true, Kind = "DWord", ValueBase64 = Convert.ToBase64String(BitConverter.GetBytes(0)) },
            new SavedValue { Hive = "HKCU", SubKey = @"Control Panel\Mouse", Name = "RawMouseThrottleDuration",
                             Existed = false, Kind = "Unknown" },
        },
        Notes = { ["prevScheme"] = "381b4222-f694-41f0-9685-ff5bb260df2e" },
    };

    [Fact]
    public void NewStore_WithoutFile_IsEmpty()
    {
        var store = new TweakStateStore(StatePath);
        Assert.Empty(store.AppliedTweakIds());
        Assert.False(store.IsApplied("mpo"));
        Assert.Null(store.GetApplied("mpo"));
        Assert.False(File.Exists(StatePath)); // nothing written until something changes
    }

    [Fact]
    public void MarkApplied_SurvivesRestart_WithAllCapturedData()
    {
        var before = DateTime.UtcNow;
        Assert.True(new TweakStateStore(StatePath).MarkApplied("mpo", SampleState()));

        var reopened = new TweakStateStore(StatePath); // simulates closing and reopening the app
        var state = reopened.GetApplied("mpo");

        Assert.NotNull(state);
        Assert.True(state.Applied);
        Assert.InRange(state.AppliedUtc!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(2, state.Saved.Count);
        Assert.Equal("OverlayTestMode", state.Saved[0].Name);
        Assert.True(state.Saved[0].Existed);
        Assert.Equal(Convert.ToBase64String(BitConverter.GetBytes(0)), state.Saved[0].ValueBase64);
        Assert.False(state.Saved[1].Existed);
        Assert.Null(state.Saved[1].ValueBase64);
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", state.Notes["prevScheme"]);
        Assert.Equal(new[] { "mpo" }, reopened.AppliedTweakIds());
    }

    [Fact]
    public void MarkReverted_RemovesEntry_AndPersists()
    {
        var store = new TweakStateStore(StatePath);
        store.MarkApplied("mpo", SampleState());
        store.MarkApplied("hags", SampleState());

        Assert.True(store.MarkReverted("mpo"));

        var reopened = new TweakStateStore(StatePath);
        Assert.False(reopened.IsApplied("mpo"));
        Assert.True(reopened.IsApplied("hags"));
    }

    [Fact]
    public void Store_KeepsDeepCopies_SoCallersCannotMutateIt()
    {
        var store = new TweakStateStore(StatePath);
        var mine = SampleState();
        store.MarkApplied("mpo", mine);

        mine.Saved.Clear();                                // caller keeps mutating its instance
        mine.Notes["prevScheme"] = "tampered";
        var copy = store.GetApplied("mpo")!;
        copy.Saved.Clear();                                // or mutates what it got back

        var fresh = store.GetApplied("mpo")!;
        Assert.Equal(2, fresh.Saved.Count);
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", fresh.Notes["prevScheme"]);
    }

    [Fact]
    public void File_IsVersionedCamelCaseJson()
    {
        new TweakStateStore(StatePath).MarkApplied("uac", SampleState());

        using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
        var root = doc.RootElement;
        Assert.Equal(TweakStateStore.CurrentVersion, root.GetProperty("version").GetInt32());
        var uac = root.GetProperty("tweaks").GetProperty("uac");
        Assert.True(uac.GetProperty("applied").GetBoolean());
        Assert.Equal("HKLM", uac.GetProperty("saved")[0].GetProperty("hive").GetString());
        Assert.Equal("DWord", uac.GetProperty("saved")[0].GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    public void CorruptFile_IsQuarantined_AndStoreStartsEmpty(string corrupt)
    {
        File.WriteAllText(StatePath, corrupt);

        var store = new TweakStateStore(StatePath);

        Assert.Empty(store.AppliedTweakIds());
        Assert.False(File.Exists(StatePath));
        var quarantined = Assert.Single(Directory.GetFiles(_dir.Path, "tweak-state.json.corrupt-*"));
        Assert.Equal(corrupt, File.ReadAllText(quarantined)); // original bytes kept for manual recovery

        Assert.True(store.MarkApplied("mpo", SampleState()));  // and the store keeps working
        Assert.True(new TweakStateStore(StatePath).IsApplied("mpo"));
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        var store = new TweakStateStore(StatePath);
        store.MarkApplied("a", SampleState());
        store.MarkApplied("b", SampleState());
        store.MarkReverted("a");

        Assert.Equal(new[] { StatePath }, Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var nested = Path.Combine(_dir.Path, "ProgramData", "STAKEOUT", "tweak-state.json");
        Assert.True(new TweakStateStore(nested).MarkApplied("mpo", SampleState()));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Save_Failure_IsReportedNotThrown()
    {
        // Parent "directory" is actually a file → the save cannot succeed.
        var blocker = _dir.File("blocker");
        File.WriteAllText(blocker, "x");
        var store = new TweakStateStore(Path.Combine(blocker, "tweak-state.json"));

        Assert.False(store.MarkApplied("mpo", SampleState()));
    }

    [Fact]
    public async Task ConcurrentMarkApplied_FromManyThreads_PersistsEverything()
    {
        // Regression: concurrent tweaks used to share and mutate state while another
        // thread serialized it, which could silently drop a rollback record.
        var store = new TweakStateStore(StatePath);
        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            var s = SampleState();
            s.Notes["i"] = i.ToString(CultureInfo.InvariantCulture);
            Assert.True(store.MarkApplied($"tweak-{i}", s));
        })));

        var reopened = new TweakStateStore(StatePath);
        Assert.Equal(64, reopened.AppliedTweakIds().Count);
        Assert.Equal("17", reopened.GetApplied("tweak-17")!.Notes["i"]);
    }

    [Fact]
    public void FileFromNewerVersion_IsReadBestEffort()
    {
        File.WriteAllText(StatePath,
            """{ "version": 99, "tweaks": { "mpo": { "applied": true, "saved": [], "notes": {} } }, "futureField": 1 }""");

        Assert.True(new TweakStateStore(StatePath).IsApplied("mpo"));
    }
}
