using Observation.Core.SystemAccess;
using Observation.Handlers.Apps;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class WingetInstallerTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsTrue_WhenVersionCommandSucceeds()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new CommandResult(0, "v1.8.0", "") };
        var installer = new WingetInstaller(runner);

        Assert.True(await installer.IsAvailableAsync());
        Assert.Equal("winget", runner.Calls[0].FileName);
        Assert.Contains("--version", runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenWingetMissing()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new CommandResult(-1, "", "not found") };
        var installer = new WingetInstaller(runner);

        Assert.False(await installer.IsAvailableAsync());
    }

    [Fact]
    public async Task InstallAsync_PassesSilentAndAgreementFlags()
    {
        var runner = new FakeCommandRunner();
        var installer = new WingetInstaller(runner);

        await installer.InstallAsync("Google.Chrome");

        var args = runner.Calls[0].Arguments;
        Assert.Contains("install", args);
        Assert.Contains("Google.Chrome", args);
        Assert.Contains("--silent", args);
        Assert.Contains("--accept-package-agreements", args);
        Assert.Contains("--accept-source-agreements", args);
    }

    [Fact]
    public async Task IsInstalledAsync_ReturnsTrue_WhenListOutputContainsPackageId()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new CommandResult(0, "Name  Id            Version\nChrome Google.Chrome 128.0", "") };
        var installer = new WingetInstaller(runner);

        Assert.True(await installer.IsInstalledAsync("Google.Chrome"));
    }

    [Fact]
    public async Task IsInstalledAsync_ReturnsFalse_WhenCommandFails()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new CommandResult(1, "", "No installed package found") };
        var installer = new WingetInstaller(runner);

        Assert.False(await installer.IsInstalledAsync("Google.Chrome"));
    }
}
