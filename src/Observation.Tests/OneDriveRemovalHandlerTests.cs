using Observation.Core.Tweaks;
using Observation.Handlers.OneDrive;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class OneDriveRemovalHandlerTests
{
    private static readonly TweakDefinition DummyTweak = new()
    {
        Id = "apps.onedrive.remove",
        Tab = "apps",
        Group = new LocalizedText { Ru = "Приложения", En = "Applications" },
        Name = new LocalizedText { Ru = "...", En = "..." },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Risky,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "RemoveOneDrive" },
        Verify = new HandlerVerifySpec { HandlerId = "RemoveOneDrive" },
        Revert = new RevertSpec { Type = RevertType.BestEffort }
    };

    [Fact]
    public async Task ApplyAsync_RefusesToReinstall_WhenDesiredOnIsFalse()
    {
        var handler = new OneDriveRemovalHandler(new FakeRegistryAccessor());

        var result = await handler.ApplyAsync(DummyTweak, desiredOn: false);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RevertAsync_AlwaysRefuses_BestEffortHasNoAutomaticRevert()
    {
        var handler = new OneDriveRemovalHandler(new FakeRegistryAccessor());

        var result = await handler.RevertAsync(DummyTweak, capturedState: null);

        Assert.False(result.Success);
    }
}
