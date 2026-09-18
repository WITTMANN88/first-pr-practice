using Observation.App.Runtime;
using Observation.App.Services;
using Observation.Core.Batch;
using Observation.Core.Conflicts;
using Observation.Core.Engine;
using Observation.Core.Journal;
using Observation.Core.Scripts;
using Observation.Core.SystemAccess;
using Observation.Handlers.Apps;
using Observation.Handlers.Debloat;
using Observation.Handlers.Diag;
using Observation.Handlers.Scripts;

namespace Observation.App.ViewModels;

/// <summary>
/// Корневая модель окна: список вкладок сайдбара (13 штук, id — как в data-panel из
/// ui-preview.html и в поле "tab" JSON-реестра), переключение текущей вкладки,
/// сворачивание сайдбара, переключение языка. Вкладки пересоздаются при каждой
/// навигации, но состояние тумблеров не теряется — оно живёт в TweakLibrary, общей
/// для всех вкладок и для «Главной».
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<NavItem> NavItems { get; }

    private readonly TweakLibrary _library;
    private readonly TweakEngine _engine;
    private readonly BatchRunner _batchRunner;
    private readonly ConflictDetector _conflictDetector;
    private readonly SystemContext _systemContext;
    private readonly IJournalStore _journal;
    private readonly PcSpecs _pcSpecs;
    private readonly IUwpPackageScanner _uwpScanner;
    private readonly IWingetInstaller _wingetInstaller;
    private readonly IReadOnlyList<ScriptDefinition> _scripts;
    private readonly PowerShellScriptRunner _scriptRunner;
    private readonly IProblemDeviceScanner _problemDeviceScanner;
    private readonly Func<string, object> _tabFactory;

    private NavItem _selectedNav;
    public NavItem SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (!SetField(ref _selectedNav, value))
                return;

            CurrentTab = _tabFactory(value.Id);
        }
    }

    private object? _currentTab;
    public object? CurrentTab
    {
        get => _currentTab;
        private set => SetField(ref _currentTab, value);
    }

    private bool _isSidebarCollapsed;
    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set => SetField(ref _isSidebarCollapsed, value);
    }

    public RelayCommand SelectNavCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public RelayCommand SetLanguageCommand { get; }

    public MainWindowViewModel(ILocalizationService localization, TweakLibrary library, TweakEngine engine, BatchRunner batchRunner,
        ConflictDetector conflictDetector, SystemContext systemContext, IJournalStore journal, PcSpecs pcSpecs,
        IUwpPackageScanner uwpScanner, IWingetInstaller wingetInstaller, IReadOnlyList<ScriptDefinition> scripts,
        PowerShellScriptRunner scriptRunner, IProblemDeviceScanner problemDeviceScanner)
    {
        Localization = localization;
        _library = library;
        _engine = engine;
        _batchRunner = batchRunner;
        _conflictDetector = conflictDetector;
        _systemContext = systemContext;
        _journal = journal;
        _pcSpecs = pcSpecs;
        _uwpScanner = uwpScanner;
        _wingetInstaller = wingetInstaller;
        _scripts = scripts;
        _scriptRunner = scriptRunner;
        _problemDeviceScanner = problemDeviceScanner;
        NavItems = BuildNavItems();
        _tabFactory = CreateTab;

        SelectNavCommand = new RelayCommand(id => SelectedNav = NavItems.First(n => n.Id == (string)id!));
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        SetLanguageCommand = new RelayCommand(lang => Localization.SetLanguage((string)lang!));

        _selectedNav = NavItems[0];
        _currentTab = _tabFactory(_selectedNav.Id);
    }

    private static IReadOnlyList<NavItem> BuildNavItems() => new[]
    {
        new NavItem { Id = "home", LocalizationKey = "NavHome" },
        new NavItem { Id = "perf", LocalizationKey = "NavPerf" },
        new NavItem { Id = "privacy", LocalizationKey = "NavPrivacy" },
        new NavItem { Id = "security", LocalizationKey = "NavSecurity" },
        new NavItem { Id = "debloat", LocalizationKey = "NavDebloat" },
        new NavItem { Id = "clean", LocalizationKey = "NavClean" },
        new NavItem { Id = "ui", LocalizationKey = "NavUi" },
        new NavItem { Id = "apps", LocalizationKey = "NavApps" },
        new NavItem { Id = "scripts", LocalizationKey = "NavScripts" },
        new NavItem { Id = "net", LocalizationKey = "NavNet" },
        new NavItem { Id = "autostart", LocalizationKey = "NavAutostart" },
        new NavItem { Id = "updates", LocalizationKey = "NavUpdates" },
        new NavItem { Id = "diag", LocalizationKey = "NavDiag" }
    };

    private object CreateTab(string tabId) => tabId switch
    {
        "home" => new HomeTabViewModel(Localization, _library, _engine, _batchRunner, _conflictDetector, _systemContext, _journal, _pcSpecs),
        "debloat" => new DebloatTabViewModel(Localization, _library.GroupsForTab(tabId), _uwpScanner),
        "apps" => new AppsTabViewModel(Localization, _library.GroupsForTab(tabId), _wingetInstaller),
        "scripts" => new ScriptsTabViewModel(Localization, _scripts, _scriptRunner),
        "clean" => new CleanTabViewModel(Localization, _library.GroupsForTab(tabId), _scripts, _scriptRunner),
        "diag" => new DiagTabViewModel(Localization, _library.GroupsForTab(tabId), _problemDeviceScanner),
        _ => new TweakListTabViewModel(_library.GroupsForTab(tabId))
    };
}
