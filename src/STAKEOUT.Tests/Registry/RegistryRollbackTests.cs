using Stakeout.Models;
using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Registry;

public class RegistryRollbackTests
{
    private const RegHive HKLM = RegHive.LocalMachine;
    private const RegHive HKCU = RegHive.CurrentUser;

    private readonly FakeRegistry _reg = new();
    private readonly RegistryRollback _rollback;

    public RegistryRollbackTests() => _rollback = new RegistryRollback(_reg);

    [Fact]
    public void CaptureAndSet_ThenRestoreAll_BringsBackTheOriginal()
    {
        _reg.Seed(HKLM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 0, RegValueKind.DWord);
        var state = new TweakState();

        Assert.True(_rollback.CaptureAndSet(state, HKLM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5, RegValueKind.DWord));
        Assert.Equal(5, _reg.Get(HKLM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode").Value);

        Assert.True(_rollback.RestoreAll(state));
        Assert.Equal(0, _reg.Get(HKLM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode").Value);
    }

    [Fact]
    public void ValueThatDidNotExist_IsDeletedOnRestore()
    {
        var state = new TweakState();
        _rollback.CaptureAndSet(state, HKCU, @"Control Panel\Mouse", "RawMouseThrottleDuration", 50, RegValueKind.DWord);
        Assert.True(_reg.Contains(HKCU, @"Control Panel\Mouse", "RawMouseThrottleDuration"));

        Assert.True(_rollback.RestoreAll(state));
        Assert.False(_reg.Contains(HKCU, @"Control Panel\Mouse", "RawMouseThrottleDuration"));
    }

    [Fact]
    public void SameValueWrittenTwice_RestoresTheTrueOriginal()
    {
        _reg.Seed(HKCU, @"Control Panel\Desktop", "MenuShowDelay", "400", RegValueKind.String);
        var state = new TweakState();
        _rollback.CaptureAndSet(state, HKCU, @"Control Panel\Desktop", "MenuShowDelay", "0", RegValueKind.String);
        _rollback.CaptureAndSet(state, HKCU, @"Control Panel\Desktop", "MenuShowDelay", "10", RegValueKind.String);

        _rollback.RestoreAll(state); // reverse order: last capture ("0") first, then the original ("400")
        Assert.Equal("400", _reg.Get(HKCU, @"Control Panel\Desktop", "MenuShowDelay").Value);
    }

    [Fact]
    public void RestoreAll_PreservesOriginalKind()
    {
        // A DWord tweak applied over a pre-existing String value must restore a String.
        _reg.Seed(HKCU, "K", "V", "text", RegValueKind.String);
        var state = new TweakState();
        _rollback.CaptureAndSet(state, HKCU, "K", "V", 1, RegValueKind.DWord);
        _rollback.RestoreAll(state);

        Assert.Equal(("text", RegValueKind.String), ((string)_reg.Get(HKCU, "K", "V").Value, _reg.Get(HKCU, "K", "V").Kind));
    }

    [Fact]
    public void ApplyAll_WhenAWriteFails_UndoesEarlierWritesAndDropsTheirCaptures()
    {
        _reg.Seed(HKLM, @"SOFTWARE\P\System", "EnableLUA", 1, RegValueKind.DWord);
        _reg.Seed(HKLM, @"SOFTWARE\P\System", "ConsentPromptBehaviorAdmin", 5, RegValueKind.DWord);
        var before = _reg.Snapshot();
        _reg.FailWhen = (_, name) => name == "PromptOnSecureDesktop";

        var state = new TweakState();
        var ok = _rollback.ApplyAll(state, new[]
        {
            new RegistryOp(HKLM, @"SOFTWARE\P\System", "EnableLUA", 0, RegValueKind.DWord),
            new RegistryOp(HKLM, @"SOFTWARE\P\System", "ConsentPromptBehaviorAdmin", 0, RegValueKind.DWord),
            new RegistryOp(HKLM, @"SOFTWARE\P\System", "PromptOnSecureDesktop", 0, RegValueKind.DWord),
        });

        Assert.False(ok);
        Assert.Empty(state.Saved);
        AssertRegistryEquals(before, _reg);
    }

    [Fact]
    public void ApplyAll_AppendsToExistingCaptures_AndOnlyUndoesItsOwn()
    {
        var state = new TweakState();
        _rollback.CaptureAndSet(state, HKCU, "K", "Earlier", 1, RegValueKind.DWord);
        _reg.FailWhen = (_, name) => name == "Fails";

        Assert.False(_rollback.ApplyAll(state, new[]
        {
            new RegistryOp(HKCU, "K", "Mine", 1, RegValueKind.DWord),
            new RegistryOp(HKCU, "K", "Fails", 1, RegValueKind.DWord),
        }));

        Assert.Single(state.Saved);                         // the earlier capture survives
        Assert.True(_reg.Contains(HKCU, "K", "Earlier"));   // and its write is untouched
        Assert.False(_reg.Contains(HKCU, "K", "Mine"));     // this call's write was undone
    }

    [Fact]
    public void SetIfExists_SkipsAbsentValues_WithoutRecordingThem()
    {
        _reg.Seed(HKLM, @"Enum\USB\VID_1\1\Device Parameters", "AllowIdleIrpInD3", 1, RegValueKind.DWord);
        var state = new TweakState();

        Assert.True(_rollback.SetIfExists(state, HKLM, @"Enum\USB\VID_1\1\Device Parameters", "AllowIdleIrpInD3", 0, RegValueKind.DWord));
        Assert.False(_rollback.SetIfExists(state, HKLM, @"Enum\USB\VID_1\1\Device Parameters", "SelectiveSuspendOn", 0, RegValueKind.DWord));

        Assert.Single(state.Saved);
        Assert.False(_reg.Contains(HKLM, @"Enum\USB\VID_1\1\Device Parameters", "SelectiveSuspendOn"));
    }

    [Fact]
    public void RestoreAll_ContinuesPastFailures_AndReportsThem()
    {
        var state = new TweakState();
        _rollback.CaptureAndSet(state, HKCU, "K", "A", 1, RegValueKind.DWord);
        _rollback.CaptureAndSet(state, HKCU, "K", "B", 1, RegValueKind.DWord);
        _reg.FailWhen = (_, name) => name == "B";

        Assert.False(_rollback.RestoreAll(state));
        Assert.False(_reg.Contains(HKCU, "K", "A")); // A was still restored (deleted)
    }

    [Fact]
    public void RestoreAll_SkipsUndecodableRecords_AndReportsFailure()
    {
        var state = new TweakState();
        _rollback.CaptureAndSet(state, HKCU, "K", "Good", 1, RegValueKind.DWord);
        state.Saved.Add(new SavedValue { Hive = "HKLM", SubKey = "K", Name = "Bad", Existed = true, Kind = "Bogus", ValueBase64 = "AA==" });

        Assert.False(_rollback.RestoreAll(state));
        Assert.False(_reg.Contains(HKCU, "K", "Good"));
        Assert.False(_reg.Contains(HKLM, "K", "Bad")); // never written as a guessed type
    }

    private static void AssertRegistryEquals(Dictionary<string, (object Value, RegValueKind Kind)> expected, FakeRegistry actual)
    {
        var now = actual.Snapshot();
        Assert.Equal(expected.Keys.OrderBy(k => k), now.Keys.OrderBy(k => k));
        foreach (var (key, (value, kind)) in expected)
        {
            Assert.Equal(kind, now[key].Kind);
            Assert.True(FakeRegistry.ValueEquals(value, now[key].Value), $"value differs at {key}");
        }
    }
}
