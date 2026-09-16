using Microsoft.Win32;
using Observation.Core.Tweaks;
using Observation.Handlers.Perf;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class GameModeHandlerTests
{
    private static readonly TweakDefinition DummyTweak = new()
    {
        Id = "perf.gamemode",
        Tab = "perf",
        Group = new LocalizedText { Ru = "...", En = "..." },
        Name = new LocalizedText { Ru = "...", En = "..." },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "GameModeToggle" },
        Verify = new HandlerVerifySpec { HandlerId = "GameModeToggle" },
        Revert = new RevertSpec { Type = RevertType.RestorePrevious }
    };

    [Fact]
    public async Task ApplyAsync_WritesBothAutoGameModeEnabledAndAllowAutoGameMode()
    {
        var registry = new FakeRegistryAccessor();
        var handler = GameModeHandler.Create(registry);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\GameBar", "AutoGameModeEnabled", out var v1, out _));
        Assert.Equal(1, v1);
        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\GameBar", "AllowAutoGameMode", out var v2, out _));
        Assert.Equal(1, v2);
    }
}
