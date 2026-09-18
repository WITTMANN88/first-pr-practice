using Microsoft.Win32;
using Observation.Core.Tweaks;
using Observation.Handlers.Ui;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class ClassicContextMenuHandlerTests
{
    private const string ClsidPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string ClsidKeyPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    private static readonly TweakDefinition DummyTweak = new()
    {
        Id = "ui.classiccontextmenu",
        Tab = "ui",
        Group = new LocalizedText { Ru = "...", En = "..." },
        Name = new LocalizedText { Ru = "...", En = "..." },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "ClassicContextMenu" },
        Verify = new HandlerVerifySpec { HandlerId = "ClassicContextMenu" },
        Revert = new RevertSpec { Type = RevertType.BestEffort }
    };

    [Fact]
    public async Task ApplyAsync_On_WritesEmptyDefaultValue()
    {
        var registry = new FakeRegistryAccessor();
        var runner = new FakeCommandRunner();
        var handler = new ClassicContextMenuHandler(registry, runner);

        var result = await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(result.Success);
        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, ClsidPath, "", out var data, out _));
        Assert.Equal("", data);
    }

    [Fact]
    public async Task ApplyAsync_Off_DeletesWholeKey()
    {
        var registry = new FakeRegistryAccessor();
        registry.Seed(RegistryHive.CurrentUser, ClsidPath, "", "", RegistryValueKind.String);
        var handler = new ClassicContextMenuHandler(registry, new FakeCommandRunner());

        var result = await handler.ApplyAsync(DummyTweak, desiredOn: false);

        Assert.True(result.Success);
        Assert.False(registry.TryReadValue(RegistryHive.CurrentUser, ClsidPath, "", out _, out _));
    }

    [Fact]
    public async Task ApplyAsync_RestartsExplorer()
    {
        var runner = new FakeCommandRunner();
        var handler = new ClassicContextMenuHandler(new FakeRegistryAccessor(), runner);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.Contains(runner.Calls, c => string.Join(" ", c.Arguments).Contains("Start-Process explorer.exe"));
    }

    [Fact]
    public async Task VerifyAsync_MatchesPresenceOfDefaultValue()
    {
        var registry = new FakeRegistryAccessor();
        var handler = new ClassicContextMenuHandler(registry, new FakeCommandRunner());

        Assert.False(await handler.VerifyAsync(DummyTweak, desiredOn: true));

        registry.Seed(RegistryHive.CurrentUser, ClsidPath, "", "", RegistryValueKind.String);
        Assert.True(await handler.VerifyAsync(DummyTweak, desiredOn: true));
        Assert.False(await handler.VerifyAsync(DummyTweak, desiredOn: false));
    }

    [Fact]
    public void DeleteKey_RemovesWholeSubtree_NotJustExactPath()
    {
        var registry = new FakeRegistryAccessor();
        registry.Seed(RegistryHive.CurrentUser, ClsidPath, "", "", RegistryValueKind.String);
        registry.Seed(RegistryHive.CurrentUser, ClsidKeyPath, "SomeOtherValue", 1, RegistryValueKind.DWord);

        registry.DeleteKey(RegistryHive.CurrentUser, ClsidKeyPath);

        Assert.False(registry.TryReadValue(RegistryHive.CurrentUser, ClsidPath, "", out _, out _));
        Assert.False(registry.TryReadValue(RegistryHive.CurrentUser, ClsidKeyPath, "SomeOtherValue", out _, out _));
    }
}
