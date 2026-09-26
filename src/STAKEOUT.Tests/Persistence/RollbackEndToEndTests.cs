using Stakeout.Models;
using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Persistence;

/// <summary>
/// The guarantee "Отменить всё" relies on, end to end: capture originals → persist
/// to tweak-state.json → app restarts → load → restore → the registry is exactly
/// what it was before, value by value and type by type.
/// </summary>
public sealed class RollbackEndToEndTests : IDisposable
{
    private const RegHive HKLM = RegHive.LocalMachine;
    private const RegHive HKCU = RegHive.CurrentUser;

    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ApplyPersistRestartRevert_RestoresRegistryExactly()
    {
        var path = _dir.File("tweak-state.json");

        var registry = new FakeRegistry();
        // Pre-existing values of every kind the codec supports.
        registry.Seed(HKLM, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 2, RegValueKind.DWord);
        registry.Seed(HKCU, @"Control Panel\Desktop", "MenuShowDelay", "400", RegValueKind.String);
        registry.Seed(HKLM, @"SYSTEM\X", "ImagePath", @"%SystemRoot%\System32\svc.exe", RegValueKind.ExpandString);
        registry.Seed(HKLM, @"SYSTEM\X", "Big", 1L << 40, RegValueKind.QWord);
        registry.Seed(HKCU, @"Control Panel\Desktop", "UserPreferencesMask", new byte[] { 0x9E, 0x3E, 0x07, 0x80 }, RegValueKind.Binary);
        registry.Seed(HKLM, @"SYSTEM\X", "Deps", new[] { "RpcSs", "", "Tcpip" }, RegValueKind.MultiString);
        var before = registry.Snapshot();

        // --- session 1: apply two tweaks and persist their rollback records ---
        var rollback = new RegistryRollback(registry);
        var store = new TweakStateStore(path);

        var t1 = new TweakState();
        Assert.True(rollback.ApplyAll(t1, new[]
        {
            new RegistryOp(HKLM, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4, RegValueKind.DWord),
            new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegValueKind.DWord), // new value
            new RegistryOp(HKCU, @"Control Panel\Desktop", "MenuShowDelay", "0", RegValueKind.String),
        }));
        Assert.True(store.MarkApplied("t1", t1));

        var t2 = new TweakState();
        Assert.True(rollback.ApplyAll(t2, new[]
        {
            new RegistryOp(HKLM, @"SYSTEM\X", "ImagePath", @"C:\evil.exe", RegValueKind.String),   // kind change too
            new RegistryOp(HKLM, @"SYSTEM\X", "Big", 0L, RegValueKind.QWord),
            new RegistryOp(HKCU, @"Control Panel\Desktop", "UserPreferencesMask", new byte[] { 0, 0 }, RegValueKind.Binary),
            new RegistryOp(HKLM, @"SYSTEM\X", "Deps", Array.Empty<string>(), RegValueKind.MultiString),
        }));
        Assert.True(store.MarkApplied("t2", t2));

        // --- session 2: a fresh process loads the file and reverts everything ---
        var restarted = new TweakStateStore(path);
        var rollback2 = new RegistryRollback(registry);
        foreach (var id in restarted.AppliedTweakIds())
        {
            Assert.True(rollback2.RestoreAll(restarted.GetApplied(id)!));
            Assert.True(restarted.MarkReverted(id));
        }

        // Registry is exactly as before (the new AllowTelemetry value is gone again).
        var after = registry.Snapshot();
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (key, (value, kind)) in before)
        {
            Assert.Equal(kind, after[key].Kind);
            Assert.True(FakeRegistry.ValueEquals(value, after[key].Value), $"value differs at {key}");
        }
        Assert.Empty(new TweakStateStore(path).AppliedTweakIds());
    }
}
