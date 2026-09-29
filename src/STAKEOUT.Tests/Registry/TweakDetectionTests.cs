using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Registry;

/// <summary>
/// Reading whether a tweak is already in effect (the Tweaks page scans this at
/// startup): value comparison against the registry, and how the result and the
/// rollback record combine into the toggle state.
/// </summary>
public class TweakDetectionTests
{
    private const RegHive HKLM = RegHive.LocalMachine;
    private const string Uac = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    private readonly FakeRegistry _reg = new();
    private readonly RegistryRollback _rollback;

    public TweakDetectionTests() => _rollback = new RegistryRollback(_reg);

    private static RegistryOp EnableLuaOff => new(HKLM, Uac, "EnableLUA", 0, RegValueKind.DWord);

    [Fact]
    public void Matches_SameDword_True()
    {
        _reg.Seed(HKLM, Uac, "EnableLUA", 0, RegValueKind.DWord);
        Assert.True(_rollback.Matches(EnableLuaOff));
    }

    [Fact]
    public void Matches_DifferentValue_False()
    {
        _reg.Seed(HKLM, Uac, "EnableLUA", 1, RegValueKind.DWord);
        Assert.False(_rollback.Matches(EnableLuaOff));
    }

    [Fact]
    public void Matches_MissingValueOrKey_IsOff()
    {
        // No key at all: the default (off), not an error.
        Assert.False(_rollback.Matches(EnableLuaOff));
    }

    [Fact]
    public void Matches_SameDataDifferentKind_False()
    {
        // MenuShowDelay is a string "0"; a DWORD 0 is not what the tweak writes.
        _reg.Seed(RegHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", 0, RegValueKind.DWord);
        Assert.False(_rollback.Matches(new RegistryOp(RegHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0", RegValueKind.String)));
    }

    [Fact]
    public void Matches_String_ComparesText()
    {
        _reg.Seed(RegHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0", RegValueKind.String);
        Assert.True(_rollback.Matches(new RegistryOp(RegHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0", RegValueKind.String)));
        Assert.False(_rollback.Matches(new RegistryOp(RegHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "400", RegValueKind.String)));
    }

    [Fact]
    public void Matches_FullRangeDword_SignednessDoesNotMatter()
    {
        // NetworkThrottlingIndex = 0xFFFFFFFF: the registry hands back int -1.
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        _reg.Seed(HKLM, key, "NetworkThrottlingIndex", -1, RegValueKind.DWord);
        Assert.True(_rollback.Matches(new RegistryOp(HKLM, key, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegValueKind.DWord)));
        Assert.True(_rollback.Matches(new RegistryOp(HKLM, key, "NetworkThrottlingIndex", 0xFFFFFFFFu, RegValueKind.DWord)));
    }

    [Fact]
    public void Matches_IsReadOnly()
    {
        _reg.Seed(HKLM, Uac, "EnableLUA", 1, RegValueKind.DWord);
        var before = _reg.WriteCount;

        _rollback.Matches(EnableLuaOff);

        Assert.Equal(before, _reg.WriteCount);
        Assert.Equal(1, _reg.Get(HKLM, Uac, "EnableLUA").Value);
    }

    [Fact]
    public void AllMatch_RequiresEveryValue()
    {
        var ops = new[]
        {
            EnableLuaOff,
            new RegistryOp(HKLM, Uac, "ConsentPromptBehaviorAdmin", 0, RegValueKind.DWord),
        };
        _reg.Seed(HKLM, Uac, "EnableLUA", 0, RegValueKind.DWord);
        Assert.False(_rollback.AllMatch(ops)); // second value missing

        _reg.Seed(HKLM, Uac, "ConsentPromptBehaviorAdmin", 0, RegValueKind.DWord);
        Assert.True(_rollback.AllMatch(ops));
    }

    [Fact]
    public void AllMatch_EmptyList_False()
    {
        Assert.False(_rollback.AllMatch(Array.Empty<RegistryOp>()));
    }

    [Fact]
    public void ApplyThenMatches_True_RestoreThenMatches_False()
    {
        _reg.Seed(HKLM, Uac, "EnableLUA", 1, RegValueKind.DWord);
        var state = new Stakeout.Models.TweakState();

        Assert.True(_rollback.ApplyAll(state, new[] { EnableLuaOff }));
        Assert.True(_rollback.Matches(EnableLuaOff));

        Assert.True(_rollback.RestoreAll(state));
        Assert.False(_rollback.Matches(EnableLuaOff));
    }

    [Theory]
    // detected, recorded → IsOn, CanRevert, AppliedOutside
    [InlineData(true, true, true, true, false)] // applied by STAKEOUT
    [InlineData(true, false, true, false, true)] // already set in Windows: on, but no originals to restore
    [InlineData(false, false, false, false, false)] // default
    [InlineData(false, true, false, true, false)] // STAKEOUT applied it, undone elsewhere: the system wins
    [InlineData(null, true, true, true, false)] // unreadable: fall back to the record
    [InlineData(null, false, false, false, false)]
    public void Resolve_TruthTable(bool? detected, bool recorded, bool isOn, bool canRevert, bool outside)
    {
        Assert.Equal(new TweakStatus(isOn, canRevert, outside), TweakStatus.Resolve(detected, recorded));
    }
}
