using Observation.App.Services;
using Observation.App.ViewModels;
using Observation.Core.Tweaks;
using Xunit;

namespace Observation.App.Tests;

public class MainWindowViewModelTests
{
    private static TweakDefinition MakeTweak(string id, string tab, string group) => new()
    {
        Id = id,
        Tab = tab,
        Group = group,
        Name = new LocalizedText { Ru = id, En = id },
        Description = new LocalizedText { Ru = "...", En = "..." },
        Severity = Severity.Safe,
        ControlType = ControlType.Toggle,
        Apply = new HandlerApplySpec { HandlerId = "Noop" },
        Verify = new HandlerVerifySpec { HandlerId = "Noop" },
        Revert = new RevertSpec { Type = RevertType.RestorePrevious }
    };

    private static IReadOnlyList<TweakDefinition> SampleTweaks() => new[]
    {
        MakeTweak("perf.gamemode", "perf", "Питание и режимы"),
        MakeTweak("apps.discord.hwaccel", "perf", "Приложения"),
        MakeTweak("privacy.diagtrack", "privacy", "Телеметрия")
    };

    [Fact]
    public void NavItems_Has13Tabs_StartingWithHome()
    {
        var vm = new MainWindowViewModel(new LocalizationService(), SampleTweaks());

        Assert.Equal(13, vm.NavItems.Count);
        Assert.Equal("home", vm.NavItems[0].Id);
    }

    [Fact]
    public void InitialTab_IsHomeTabViewModel()
    {
        var vm = new MainWindowViewModel(new LocalizationService(), SampleTweaks());

        Assert.IsType<HomeTabViewModel>(vm.CurrentTab);
    }

    [Fact]
    public void SelectingPerfTab_FiltersOnlyMatchingTweaks()
    {
        var vm = new MainWindowViewModel(new LocalizationService(), SampleTweaks());

        vm.SelectedNav = vm.NavItems.Single(n => n.Id == "perf");

        var tab = Assert.IsType<TweakListTabViewModel>(vm.CurrentTab);
        var allIds = tab.Groups.SelectMany(g => g.Items).Select(i => i.Definition.Id).ToList();
        Assert.Contains("perf.gamemode", allIds);
        Assert.Contains("apps.discord.hwaccel", allIds);
        Assert.DoesNotContain("privacy.diagtrack", allIds);
    }

    [Fact]
    public void SelectingTabWithNoTweaks_ProducesEmptyTabViewModel()
    {
        var vm = new MainWindowViewModel(new LocalizationService(), SampleTweaks());

        vm.SelectedNav = vm.NavItems.Single(n => n.Id == "diag");

        var tab = Assert.IsType<TweakListTabViewModel>(vm.CurrentTab);
        Assert.True(tab.IsEmpty);
    }

    [Fact]
    public void ToggleSidebarCommand_FlipsIsSidebarCollapsed()
    {
        var vm = new MainWindowViewModel(new LocalizationService(), SampleTweaks());

        vm.ToggleSidebarCommand.Execute(null);
        Assert.True(vm.IsSidebarCollapsed);

        vm.ToggleSidebarCommand.Execute(null);
        Assert.False(vm.IsSidebarCollapsed);
    }

    [Fact]
    public void SetLanguageCommand_DelegatesToLocalizationService()
    {
        var localization = new LocalizationService();
        var vm = new MainWindowViewModel(localization, SampleTweaks());

        vm.SetLanguageCommand.Execute("en");

        Assert.Equal("en", localization.CurrentLanguage);
    }

    [Fact]
    public void SelectNavCommand_ByStringId_SwitchesSelectedNav()
    {
        var vm = new MainWindowViewModel(new LocalizationService(), SampleTweaks());

        vm.SelectNavCommand.Execute("privacy");

        Assert.Equal("privacy", vm.SelectedNav.Id);
    }
}
