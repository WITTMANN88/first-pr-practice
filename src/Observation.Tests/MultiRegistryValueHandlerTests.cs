using Microsoft.Win32;
using Observation.Core.Tweaks;
using Observation.Handlers;
using Observation.Handlers.Browsers;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class MultiRegistryValueHandlerTests
{
    private static readonly TweakDefinition DummyTweak = new()
    {
        Id = "apps.brave.debloat",
        Tab = "apps",
        Group = new LocalizedText { Ru = "Приложения", En = "Applications" },
        Name = new LocalizedText { Ru = "...", En = "..." },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "BraveDebloat" },
        Verify = new HandlerVerifySpec { HandlerId = "BraveDebloat" },
        Revert = new RevertSpec { Type = RevertType.RestorePrevious }
    };

    [Fact]
    public async Task Apply_WritesAllPoliciesAsDisabled_WhenTurnedOn()
    {
        var registry = new FakeRegistryAccessor();
        var handler = BraveDebloatHandler.Create(registry);

        var result = await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(result.Success);
        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\BraveSoftware\Brave", "BraveRewardsDisabled", out var v, out _));
        Assert.Equal(1, v);
    }

    [Fact]
    public async Task Verify_ReturnsTrue_AfterApply()
    {
        var registry = new FakeRegistryAccessor();
        var handler = BraveDebloatHandler.Create(registry);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(await handler.VerifyAsync(DummyTweak, desiredOn: true));
        Assert.False(await handler.VerifyAsync(DummyTweak, desiredOn: false));
    }

    [Fact]
    public async Task Revert_RestoresPreviousValues()
    {
        var registry = new FakeRegistryAccessor();
        registry.Seed(RegistryHive.LocalMachine, @"SOFTWARE\Policies\BraveSoftware\Brave", "BraveRewardsDisabled", 0, RegistryValueKind.DWord);
        var handler = BraveDebloatHandler.Create(registry);

        var applyResult = await handler.ApplyAsync(DummyTweak, desiredOn: true);
        var revertResult = await handler.RevertAsync(DummyTweak, applyResult.CapturedState);

        Assert.True(revertResult.Success);
        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\BraveSoftware\Brave", "BraveRewardsDisabled", out var v, out _));
        Assert.Equal(0, v);
    }

    [Fact]
    public async Task Revert_DeletesValue_WhenItDidNotExistBefore()
    {
        var registry = new FakeRegistryAccessor();
        var handler = BraveDebloatHandler.Create(registry);

        var applyResult = await handler.ApplyAsync(DummyTweak, desiredOn: true);
        await handler.RevertAsync(DummyTweak, applyResult.CapturedState);

        Assert.False(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\BraveSoftware\Brave", "BraveRewardsDisabled", out _, out _));
    }

    [Fact]
    public async Task EdgeDebloat_UsesPerPolicyOnOffValues_ForEnabledPolicies()
    {
        var registry = new FakeRegistryAccessor();
        var handler = EdgeDebloatHandler.Create(registry);

        // desiredOn=true means "деблоат включён" => фичи должны быть выключены => *Enabled=0 (OnValue: 0).
        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled", out var v, out _));
        Assert.Equal(0, v);
    }
}
