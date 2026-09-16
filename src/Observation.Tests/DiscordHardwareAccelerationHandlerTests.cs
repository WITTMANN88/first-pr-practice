using Observation.Core.Tweaks;
using Observation.Handlers.Discord;
using Xunit;

namespace Observation.Tests;

public class DiscordHardwareAccelerationHandlerTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "ObservationTests_discord_" + Guid.NewGuid() + ".json");

    private static readonly TweakDefinition DummyTweak = new()
    {
        Id = "apps.discord.hwaccel",
        Tab = "performance",
        Group = "Приложения",
        Name = new LocalizedText { Ru = "...", En = "..." },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "SetDiscordHardwareAcceleration" },
        Verify = new HandlerVerifySpec { HandlerId = "SetDiscordHardwareAcceleration" },
        Revert = new RevertSpec { Type = RevertType.RestorePrevious }
    };

    [Fact]
    public async Task Apply_SetsKey_AndPreservesUnrelatedKeys()
    {
        File.WriteAllText(_settingsPath, """{"enableHardwareAcceleration":false,"WINDOW_BOUNDS":{"x":0,"y":0},"audioSubsystem":"legacy"}""");
        var handler = new DiscordHardwareAccelerationHandler(_settingsPath);

        var result = await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(result.Success);
        var content = File.ReadAllText(_settingsPath);
        Assert.Contains("\"audioSubsystem\":\"legacy\"", content);
        Assert.True(await handler.VerifyAsync(DummyTweak, desiredOn: true));
    }

    [Fact]
    public async Task Apply_CapturesPreviousValue_ForRevert()
    {
        File.WriteAllText(_settingsPath, """{"enableHardwareAcceleration":false}""");
        var handler = new DiscordHardwareAccelerationHandler(_settingsPath);

        var applyResult = await handler.ApplyAsync(DummyTweak, desiredOn: true);
        var revertResult = await handler.RevertAsync(DummyTweak, applyResult.CapturedState);

        Assert.True(revertResult.Success);
        Assert.True(await handler.VerifyAsync(DummyTweak, desiredOn: false));
    }

    [Fact]
    public async Task Apply_RemovesKey_OnRevert_WhenKeyDidNotExistBefore()
    {
        File.WriteAllText(_settingsPath, """{"WINDOW_BOUNDS":{"x":0,"y":0}}""");
        var handler = new DiscordHardwareAccelerationHandler(_settingsPath);

        var applyResult = await handler.ApplyAsync(DummyTweak, desiredOn: true);
        Assert.Null(applyResult.CapturedState);

        await handler.RevertAsync(DummyTweak, applyResult.CapturedState);

        Assert.DoesNotContain("enableHardwareAcceleration", File.ReadAllText(_settingsPath));
    }

    [Fact]
    public async Task Apply_Fails_WhenSettingsFileMissing()
    {
        var handler = new DiscordHardwareAccelerationHandler(_settingsPath);

        var result = await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.False(result.Success);
    }

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }
}
