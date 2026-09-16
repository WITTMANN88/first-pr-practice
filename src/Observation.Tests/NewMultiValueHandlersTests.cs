using Microsoft.Win32;
using Observation.Core.Tweaks;
using Observation.Handlers.Perf;
using Observation.Handlers.Privacy;
using Observation.Handlers.Security;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

/// <summary>Проверяет только конфигурацию (пути/OnValue/OffValue) новых MultiRegistryValueHandler-обёрток — сам механизм уже покрыт MultiRegistryValueHandlerTests.</summary>
public class NewMultiValueHandlersTests
{
    private static readonly TweakDefinition DummyTweak = new()
    {
        Id = "dummy",
        Tab = "perf",
        Group = new LocalizedText { Ru = "...", En = "..." },
        Name = new LocalizedText { Ru = "...", En = "..." },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "x" },
        Verify = new HandlerVerifySpec { HandlerId = "x" },
        Revert = new RevertSpec { Type = RevertType.RestorePrevious }
    };

    [Fact]
    public async Task XboxGameBar_WritesBothValuesAsDisabled_WhenOn()
    {
        var registry = new FakeRegistryAccessor();
        var handler = XboxGameBarHandler.Create(registry);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", out var v1, out _));
        Assert.Equal(0, v1);
        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", out var v2, out _));
        Assert.Equal(0, v2);
    }

    [Fact]
    public async Task BrowserPerformance_WritesAllFourValues_ForChromeAndEdge()
    {
        var registry = new FakeRegistryAccessor();
        var handler = BrowserPerformanceHandler.Create(registry);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "HardwareAccelerationModeEnabled", out var v1, out _));
        Assert.Equal(0, v1);
        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", out var v2, out _));
        Assert.Equal(0, v2);
    }

    [Fact]
    public async Task UacSlider_UsesDocumentedWindowsDefaults_OnRevert()
    {
        var registry = new FakeRegistryAccessor();
        var handler = UacSliderHandler.Create(registry);

        var apply = await handler.ApplyAsync(DummyTweak, desiredOn: true);
        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", out var onValue, out _));
        Assert.Equal(0, onValue);

        await handler.RevertAsync(DummyTweak, apply.CapturedState);
        // Ключей не было — revert должен удалить, а не поставить "5 по умолчанию" поверх реального prior-state.
        Assert.False(registry.TryReadValue(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", out _, out _));
    }

    [Fact]
    public async Task WindowsCopilot_WritesTurnOffFlag_UnderBothHives()
    {
        var registry = new FakeRegistryAccessor();
        var handler = WindowsCopilotHandler.Create(registry);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", out var v1, out _));
        Assert.Equal(1, v1);
        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", out var v2, out _));
        Assert.Equal(1, v2);
    }

    [Fact]
    public async Task WebSearch_DisablesBingAndCortanaConsent_WhenOn()
    {
        var registry = new FakeRegistryAccessor();
        var handler = WebSearchHandler.Create(registry);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(registry.TryReadValue(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", out var v, out _));
        Assert.Equal(0, v);
    }

    [Fact]
    public async Task ActivityHistory_DisablesAllThreeFlags_WhenOn()
    {
        var registry = new FakeRegistryAccessor();
        var handler = ActivityHistoryHandler.Create(registry);

        await handler.ApplyAsync(DummyTweak, desiredOn: true);

        Assert.True(registry.TryReadValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", out var v, out _));
        Assert.Equal(0, v);
    }
}
