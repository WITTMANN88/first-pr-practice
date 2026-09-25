using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Stakeout.Core;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>Which page is shown in the content host.</summary>
public enum Page { SysInfo, Tweaks, Uwp, Software }

/// <summary>
/// Application shell view model. Constructs the service graph, owns the four page
/// view models, drives navigation, the sidebar collapse state, the toast stack
/// and the global "revert all" action.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly TweakService _tweakService;
    private readonly Dispatcher _dispatcher;

    private object _currentPage = null!;
    private Page _selected = Page.SysInfo;
    private bool _isSidebarCollapsed;

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // --- service graph ---
        var toast = new ToastService();
        var store = new TweakStateStore();
        var yandex = new YandexBlockService(store);
        _tweakService = new TweakService(store, yandex);

        var sysService = new SystemInfoService();
        var uwpService = new UwpService();
        var download = new DownloadService();
        var softwareService = new SoftwareInstallService(download);

        // --- page view models ---
        SysInfo = new SysInfoViewModel(sysService);
        Tweaks = new TweaksViewModel(_tweakService, toast);
        Uwp = new UwpViewModel(uwpService, toast);
        Software = new SoftwareViewModel(softwareService, toast);
        CurrentPage = SysInfo;

        // --- commands ---
        NavigateCommand = new RelayCommand(p => Navigate(Enum.Parse<Page>(p!.ToString()!)));
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        RevertAllCommand = new AsyncRelayCommand(_ => RevertAllAsync());

        // --- toast plumbing (marshal to UI thread, auto-expire after 5s) ---
        toast.Raised += OnToastRaised;
        _toast = toast;
    }

    private readonly ToastService _toast;

    // Page view models
    public SysInfoViewModel SysInfo { get; }
    public TweaksViewModel Tweaks { get; }
    public UwpViewModel Uwp { get; }
    public SoftwareViewModel Software { get; }

    public ObservableCollection<ToastMessage> Toasts { get; } = new();

    public RelayCommand NavigateCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public AsyncRelayCommand RevertAllCommand { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public Page Selected
    {
        get => _selected;
        private set => SetProperty(ref _selected, value);
    }

    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set => SetProperty(ref _isSidebarCollapsed, value);
    }

    /// <summary>Called once from the window Loaded event to kick off first load.</summary>
    public async Task InitializeAsync()
    {
        await SysInfo.LoadAsync();
    }

    private void Navigate(Page page)
    {
        Selected = page;
        CurrentPage = page switch
        {
            Page.SysInfo => SysInfo,
            Page.Tweaks => Tweaks,
            Page.Uwp => Uwp,
            Page.Software => Software,
            _ => SysInfo
        };

        // Lazy-load the UWP list the first time that page is opened.
        if (page == Page.Uwp && Uwp.Apps.Count == 0 && !Uwp.IsLoading)
            _ = Uwp.LoadAsync();
    }

    private async Task RevertAllAsync()
    {
        var count = await _tweakService.RevertAllAsync();
        Tweaks.SyncAll();
        _toast.Success($"Откат выполнен: {count} твик(ов)");
    }

    private void OnToastRaised(ToastMessage msg)
    {
        _dispatcher.Invoke(() =>
        {
            Toasts.Add(msg);
            // Auto-remove this toast after 5 seconds (single-shot).
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (s, _) =>
            {
                ((DispatcherTimer)s!).Stop();
                Toasts.Remove(msg);
            };
            timer.Start();
        });
    }
}
