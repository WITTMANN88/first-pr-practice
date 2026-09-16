using System.Text.Json;
using Microsoft.Win32;
using Observation.Core.Engine;
using Observation.Core.Handlers;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class TweakEngineTests
{
    private static readonly SystemContext DefaultContext = new(22631, "Professional", "23H2", "AMD Ryzen 7 7800X3D");

    private static TweakDefinition GameModeTweak(RevertType revertType = RevertType.RestorePrevious, ApplicabilityRule? applicability = null) => new()
    {
        Id = "perf.gamemode",
        Tab = "performance",
        Group = "Питание и режимы",
        Name = new LocalizedText { Ru = "Режим игры", En = "Game Mode" },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Applicability = applicability ?? ApplicabilityRule.Always,
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
        Revert = new RevertSpec { Type = revertType }
    };

    private static TweakDefinition DiscordHwAccelTweak() => new()
    {
        Id = "apps.discord.hwaccel",
        Tab = "performance",
        Group = "Приложения",
        Name = new LocalizedText { Ru = "Discord HW-ускорение", En = "Discord HW acceleration" },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "SetDiscordHardwareAcceleration" },
        Verify = new HandlerVerifySpec { HandlerId = "SetDiscordHardwareAcceleration" },
        Revert = new RevertSpec { Type = RevertType.RestorePrevious }
    };

    [Fact]
    public async Task ApplyAsync_RegistryTweak_WritesExpectedValueAndSucceeds()
    {
        var registry = new FakeRegistryAccessor();
        var engine = new TweakEngine(registry, new Dictionary<string, ITweakHandler>());
        var tweak = GameModeTweak();

        var outcome = await engine.ApplyAsync(tweak, desiredOn: true, DefaultContext);

        Assert.True(outcome.Success);
        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", out var data, out _));
        Assert.Equal(1, data);
    }

    [Fact]
    public async Task ApplyAsync_CapturesPreviousValue_ForRestorePreviousRevert()
    {
        var registry = new FakeRegistryAccessor();
        registry.Seed(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 0, RegistryValueKind.DWord);
        var engine = new TweakEngine(registry, new Dictionary<string, ITweakHandler>());
        var tweak = GameModeTweak();

        var applyOutcome = await engine.ApplyAsync(tweak, desiredOn: true, DefaultContext);
        Assert.True(applyOutcome.Success);

        var revertOutcome = await engine.RevertAsync(tweak, applyOutcome.PreviousValueJson);

        Assert.True(revertOutcome.Success);
        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", out var data, out _));
        Assert.Equal(0, data);
    }

    [Fact]
    public async Task ApplyAsync_RevertToMissingKey_DeletesValue()
    {
        var registry = new FakeRegistryAccessor();
        var engine = new TweakEngine(registry, new Dictionary<string, ITweakHandler>());
        var tweak = GameModeTweak();

        var applyOutcome = await engine.ApplyAsync(tweak, desiredOn: true, DefaultContext);
        await engine.RevertAsync(tweak, applyOutcome.PreviousValueJson);

        Assert.False(registry.TryReadValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", out _, out _));
    }

    [Fact]
    public async Task ApplyAsync_ReadFailureDuringCapture_SkipsApply()
    {
        var registry = new FakeRegistryAccessor { ThrowOnRead = true };
        var engine = new TweakEngine(registry, new Dictionary<string, ITweakHandler>());
        var tweak = GameModeTweak();

        var outcome = await engine.ApplyAsync(tweak, desiredOn: true, DefaultContext);

        Assert.False(outcome.Success);

        registry.ThrowOnRead = false;
        Assert.False(registry.TryReadValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", out _, out _));
    }

    [Fact]
    public async Task ApplyAsync_NotApplicable_WhenBelowMinBuild()
    {
        var registry = new FakeRegistryAccessor();
        var engine = new TweakEngine(registry, new Dictionary<string, ITweakHandler>());
        var tweak = GameModeTweak(applicability: new ApplicabilityRule { MinBuild = 99999 });

        var outcome = await engine.ApplyAsync(tweak, desiredOn: true, DefaultContext);

        Assert.False(outcome.Success);
        Assert.True(outcome.Skipped);
        Assert.False(registry.TryReadValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", out _, out _));
    }

    [Fact]
    public async Task ApplyAsync_HandlerTweak_DelegatesToHandler()
    {
        var handler = new FakeTweakHandler();
        var engine = new TweakEngine(new FakeRegistryAccessor(), new Dictionary<string, ITweakHandler> { ["SetDiscordHardwareAcceleration"] = handler });
        var tweak = DiscordHwAccelTweak();

        var outcome = await engine.ApplyAsync(tweak, desiredOn: true, DefaultContext);

        Assert.True(outcome.Success);
        Assert.True(handler.CurrentValue);
    }

    [Fact]
    public async Task ApplyAsync_HandlerFailure_PropagatesAsFailedOutcome()
    {
        var handler = new FakeTweakHandler { FailApply = true };
        var engine = new TweakEngine(new FakeRegistryAccessor(), new Dictionary<string, ITweakHandler> { ["SetDiscordHardwareAcceleration"] = handler });
        var tweak = DiscordHwAccelTweak();

        var outcome = await engine.ApplyAsync(tweak, desiredOn: true, DefaultContext);

        Assert.False(outcome.Success);
    }

    [Fact]
    public async Task RevertAsync_BestEffortRevertType_IsRefusedExplicitly()
    {
        var engine = new TweakEngine(new FakeRegistryAccessor(), new Dictionary<string, ITweakHandler>());
        var tweak = GameModeTweak(revertType: RevertType.BestEffort);

        var outcome = await engine.RevertAsync(tweak, previousValueJson: null);

        Assert.False(outcome.Success);
    }

    [Fact]
    public async Task VerifyAsync_MatchesCurrentRegistryState()
    {
        var registry = new FakeRegistryAccessor();
        var engine = new TweakEngine(registry, new Dictionary<string, ITweakHandler>());
        var tweak = GameModeTweak();

        await engine.ApplyAsync(tweak, desiredOn: true, DefaultContext);

        Assert.True(await engine.VerifyAsync(tweak, desiredOn: true));
        Assert.False(await engine.VerifyAsync(tweak, desiredOn: false));
    }
}
