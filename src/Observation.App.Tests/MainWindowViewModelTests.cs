using System.IO;
using System.Linq;
using Microsoft.Win32;
using Observation.App.Runtime;
using Observation.App.Services;
using Observation.App.ViewModels;
using Observation.Core.Batch;
using Observation.Core.Conflicts;
using Observation.Core.Engine;
using Observation.Core.Handlers;
using Observation.Core.Journal;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;
using Xunit;

namespace Observation.App.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _dataFolder = Path.Combine(Path.GetTempPath(), "ObservationAppTests_" + Guid.NewGuid());

    private static TweakDefinition MakeTweak(string id, string tab, string group) => new()
    {
        Id = id,
        Tab = tab,
        Group = new LocalizedText { Ru = group, En = group },
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

    private MainWindowViewModel CreateViewModel(ILocalizationService localization, IReadOnlyList<TweakDefinition>? tweaks = null)
    {
        var journal = new JsonLinesJournalStore(_dataFolder);
        var engine = new TweakEngine(new NoopRegistryAccessor(), new Dictionary<string, ITweakHandler>());
        var library = new TweakLibrary(localization, tweaks ?? SampleTweaks(), journal);
        var batchRunner = new BatchRunner(engine, journal);
        var systemContext = new SystemContext(0, "Core", "1.0", null);

        return new MainWindowViewModel(localization, library, engine, batchRunner, new ConflictDetector(), systemContext);
    }

    [Fact]
    public void NavItems_Has13Tabs_StartingWithHome()
    {
        var vm = CreateViewModel(new LocalizationService());

        Assert.Equal(13, vm.NavItems.Count);
        Assert.Equal("home", vm.NavItems[0].Id);
    }

    [Fact]
    public void InitialTab_IsHomeTabViewModel()
    {
        var vm = CreateViewModel(new LocalizationService());

        Assert.IsType<HomeTabViewModel>(vm.CurrentTab);
    }

    [Fact]
    public void SelectingPerfTab_FiltersOnlyMatchingTweaks()
    {
        var vm = CreateViewModel(new LocalizationService());

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
        var vm = CreateViewModel(new LocalizationService());

        vm.SelectedNav = vm.NavItems.Single(n => n.Id == "diag");

        var tab = Assert.IsType<TweakListTabViewModel>(vm.CurrentTab);
        Assert.True(tab.IsEmpty);
    }

    [Fact]
    public void NavigatingAwayAndBack_PreservesToggleState()
    {
        var vm = CreateViewModel(new LocalizationService());

        vm.SelectedNav = vm.NavItems.Single(n => n.Id == "perf");
        var firstVisit = (TweakListTabViewModel)vm.CurrentTab!;
        var item = firstVisit.Groups.SelectMany(g => g.Items).First(i => i.Definition.Id == "perf.gamemode");
        item.IsOn = true;

        vm.SelectedNav = vm.NavItems.Single(n => n.Id == "home");
        vm.SelectedNav = vm.NavItems.Single(n => n.Id == "perf");

        var secondVisit = (TweakListTabViewModel)vm.CurrentTab!;
        var sameItem = secondVisit.Groups.SelectMany(g => g.Items).First(i => i.Definition.Id == "perf.gamemode");
        Assert.True(sameItem.IsOn);
    }

    [Fact]
    public void ToggleSidebarCommand_FlipsIsSidebarCollapsed()
    {
        var vm = CreateViewModel(new LocalizationService());

        vm.ToggleSidebarCommand.Execute(null);
        Assert.True(vm.IsSidebarCollapsed);

        vm.ToggleSidebarCommand.Execute(null);
        Assert.False(vm.IsSidebarCollapsed);
    }

    [Fact]
    public void SetLanguageCommand_DelegatesToLocalizationService()
    {
        var localization = new LocalizationService();
        var vm = CreateViewModel(localization);

        vm.SetLanguageCommand.Execute("en");

        Assert.Equal("en", localization.CurrentLanguage);
    }

    [Fact]
    public void SelectNavCommand_ByStringId_SwitchesSelectedNav()
    {
        var vm = CreateViewModel(new LocalizationService());

        vm.SelectNavCommand.Execute("privacy");

        Assert.Equal("privacy", vm.SelectedNav.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
            Directory.Delete(_dataFolder, recursive: true);
    }

    private sealed class NoopRegistryAccessor : IRegistryAccessor
    {
        public bool TryReadValue(RegistryHive hive, string path, string valueName, out object? data, out RegistryValueKind kind)
        {
            data = null;
            kind = RegistryValueKind.Unknown;
            return false;
        }

        public void WriteValue(RegistryHive hive, string path, string valueName, object data, RegistryValueKind kind)
        {
        }

        public void DeleteValue(RegistryHive hive, string path, string valueName)
        {
        }
    }
}
