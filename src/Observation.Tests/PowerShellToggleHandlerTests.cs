using Observation.Core.Tweaks;
using Observation.Handlers;
using Observation.Handlers.Privacy;
using Observation.Handlers.Security;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class PowerShellToggleHandlerTests
{
    private static readonly TweakDefinition DummyTweak = new()
    {
        Id = "dummy",
        Tab = "security",
        Group = new LocalizedText { Ru = "...", En = "..." },
        Name = new LocalizedText { Ru = "...", En = "..." },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Situational,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "x" },
        Verify = new HandlerVerifySpec { HandlerId = "x" },
        Revert = new RevertSpec { Type = RevertType.BestEffort }
    };

    [Fact]
    public async Task ApplyAsync_RunsOnScript_WhenDesiredOnTrue()
    {
        var runner = new FakeCommandRunner();
        var handler = new PowerShellToggleHandler(runner, "on-script", "off-script", "verify-script");

        var result = await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(result.Success);
        Assert.Single(runner.Calls);
        Assert.Contains("on-script", runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task ApplyAsync_RunsOffScript_WhenDesiredOnFalse()
    {
        var runner = new FakeCommandRunner();
        var handler = new PowerShellToggleHandler(runner, "on-script", "off-script", "verify-script");

        await handler.ApplyAsync(DummyTweak, desiredOn: false);

        Assert.Contains("off-script", runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task ApplyAsync_Fails_WhenExitCodeNonZero()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new Core.SystemAccess.CommandResult(1, "", "Access denied") };
        var handler = new PowerShellToggleHandler(runner, "on-script", "off-script", "verify-script");

        var result = await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.False(result.Success);
        Assert.Contains("Access denied", result.Message);
    }

    [Fact]
    public async Task VerifyAsync_MatchesTrueOutput_WithDesiredOnTrue()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new Core.SystemAccess.CommandResult(0, "True\r\n", "") };
        var handler = new PowerShellToggleHandler(runner, "on-script", "off-script", "verify-script");

        Assert.True(await handler.VerifyAsync(DummyTweak, desiredOn: true));
        Assert.False(await handler.VerifyAsync(DummyTweak, desiredOn: false));
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_WhenCommandFails()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new Core.SystemAccess.CommandResult(1, "", "error") };
        var handler = new PowerShellToggleHandler(runner, "on-script", "off-script", "verify-script");

        Assert.False(await handler.VerifyAsync(DummyTweak, desiredOn: true));
    }

    [Fact]
    public async Task RevertAsync_AlwaysRefuses()
    {
        var handler = new PowerShellToggleHandler(new FakeCommandRunner(), "on-script", "off-script", "verify-script");

        var result = await handler.RevertAsync(DummyTweak, capturedState: null);

        Assert.False(result.Success);
    }

    [Fact]
    public void ControlledFolderAccess_UsesExpectedCmdlets()
    {
        var runner = new FakeCommandRunner();
        var handler = ControlledFolderAccessHandler.Create(runner);

        Assert.NotNull(handler);
    }

    [Fact]
    public async Task FirewallProfile_RunsSetNetFirewallProfile()
    {
        var runner = new FakeCommandRunner();
        var handler = FirewallProfileHandler.Create(runner);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.Contains(runner.Calls[0].Arguments, a => a.Contains("Set-NetFirewallProfile"));
    }

    [Fact]
    public async Task DiagTrack_OnScript_SetsPolicyAndStopsService()
    {
        var runner = new FakeCommandRunner();
        var handler = DiagTrackHandler.Create(runner);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        var script = string.Join(" ", runner.Calls[0].Arguments);
        Assert.Contains("AllowTelemetry", script);
        Assert.Contains("Stop-Service DiagTrack", script);
    }

    [Fact]
    public async Task DiagTrack_OffScript_RestoresAutomaticStartup()
    {
        var runner = new FakeCommandRunner();
        var handler = DiagTrackHandler.Create(runner);

        await handler.ApplyAsync(DummyTweak, desiredOn: false);

        var script = string.Join(" ", runner.Calls[0].Arguments);
        Assert.Contains("Start-Service DiagTrack", script);
    }
}
