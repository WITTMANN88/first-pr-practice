using Observation.App.Services;
using Observation.Core.Tweaks;

namespace Observation.App.ViewModels;

/// <summary>
/// Корневая модель окна: список вкладок сайдбара (13 штук, id — как в data-panel из
/// ui-preview.html и в поле "tab" JSON-реестра), переключение текущей вкладки,
/// сворачивание сайдбара, переключение языка.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<NavItem> NavItems { get; }

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

    public MainWindowViewModel(ILocalizationService localization, IReadOnlyList<TweakDefinition> tweaks)
    {
        Localization = localization;
        NavItems = BuildNavItems();
        _tabFactory = tabId => CreateTab(tabId, tweaks);

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

    private object CreateTab(string tabId, IReadOnlyList<TweakDefinition> allTweaks) =>
        tabId == "home"
            ? new HomeTabViewModel(Localization)
            : new TweakListTabViewModel(Localization, allTweaks.Where(t => t.Tab == tabId).ToList());
}
