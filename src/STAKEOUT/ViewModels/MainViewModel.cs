using System.Collections.ObjectModel;
using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Mvvm;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>Which page is shown in the content host.</summary>
public enum Page { SysInfo, Tweaks, Uwp, Software }

/// <summary>
/// Application shell view model: navigation, sidebar collapse state, the toast
/// stack (bound to <see cref="INotificationFeed"/>) and "revert all".
/// Everything is constructor-injected by the composition root in App.xaml.cs.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly TweakService _tweakService;
    private readonly INotificationService _notify;

    private object _currentPage;
    private Page _selected = Page.SysInfo;
    private bool _isSidebarCollapsed;

    public MainViewModel(
        SysInfoViewModel sysInfo,
        TweaksViewModel tweaks,
        UwpViewModel uwp,
        SoftwareViewModel software,
        LogViewerViewModel logs,
        TweakService tweakService,
        INotificationService notify,
        INotificationFeed feed)
    {
        SysInfo = sysInfo;
        Tweaks = tweaks;
        Uwp = uwp;
        Software = software;
        Logs = logs;
        _tweakService = tweakService;
        _notify = notify;
        Toasts = feed.Active;
        _currentPage = SysInfo;

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        RevertAllCommand = new AsyncRelayCommand(_ => RevertAllAsync());
    }

    // Page view models
    public SysInfoViewModel SysInfo { get; }
    public TweaksViewModel Tweaks { get; }
    public UwpViewModel Uwp { get; }
    public SoftwareViewModel Software { get; }

    /// <summary>In-app log viewer overlay (sidebar "Логи").</summary>
    public LogViewerViewModel Logs { get; }

    /// <summary>Active toasts (bottom-right stack), owned by the notification service.</summary>
    public ReadOnlyObservableCollection<Notification> Toasts { get; }

    public RelayCommand ToggleSidebarCommand { get; }
    public AsyncRelayCommand RevertAllCommand { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    /// <summary>
    /// The shown page. Settable, so the navigation radio buttons bind to it: a
    /// click, the keyboard or UI Automation's Select all switch the page, not
    /// only a mouse click on a command.
    /// </summary>
    public Page Selected
    {
        get => _selected;
        set => Navigate(value);
    }

    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set => SetProperty(ref _isSidebarCollapsed, value);
    }

    /// <summary>Called once from the window Loaded event to kick off first load.</summary>
    public Task InitializeAsync() => SysInfo.LoadAsync();

    /// <summary>
    /// Called once when the splash has gone: report startup events the user must
    /// know about. A restored state file means the last save was lost or damaged,
    /// so the toggles reflect the newest backup, not necessarily the last action.
    /// </summary>
    public void AnnounceStartupState()
    {
        if (_tweakService.StateRecoveredFrom is { } backup)
            _notify.Warning(string.Format(CultureInfo.CurrentCulture, Strings.State_RecoveredFromBackup, System.IO.Path.GetFileName(backup)));
    }

    /// <summary>Release timers/resources on shutdown.</summary>
    public void Shutdown() => SysInfo.StopLivePolling();

    private void Navigate(Page page)
    {
        SetProperty(ref _selected, page, nameof(Selected));
        CurrentPage = page switch
        {
            Page.SysInfo => SysInfo,
            Page.Tweaks => Tweaks,
            Page.Uwp => Uwp,
            Page.Software => Software,
            _ => SysInfo,
        };

        // Lazy-load the UWP list the first time that page is opened.
        if (page == Page.Uwp && Uwp.Apps.Count == 0 && !Uwp.IsLoading)
            _ = Uwp.LoadAsync();
    }

    private async Task RevertAllAsync()
    {
        var result = await _tweakService.RevertAllAsync();
        Tweaks.SyncAll();

        if (result.Complete)
            _notify.Success(string.Format(CultureInfo.CurrentCulture, Strings.Tweaks_RevertAllDone, result.Reverted));
        else
            _notify.Warning(string.Format(CultureInfo.CurrentCulture, Strings.Tweaks_RevertAllPartial, result.Reverted, result.Total));
    }
}
