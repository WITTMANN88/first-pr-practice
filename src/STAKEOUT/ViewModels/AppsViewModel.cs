using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Mvvm;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>
/// One row of the Apps table (a UWP package or a desktop program): checkbox,
/// icon, kind and category badges, size, busy and removal states.
/// </summary>
public sealed class AppItemViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isRemoving;
    private bool _isUninstalling;
    private bool _failed;
    private ImageSource? _icon;

    public AppItemViewModel(InstalledApp app) => App = app;

    public InstalledApp App { get; }
    public string DisplayName => App.DisplayName;

    /// <summary>Screen readers name the row by this (name, kind, badge, size), not by the type name.</summary>
    public override string ToString() => IsDesktop
        ? $"{DisplayName}, {KindText}, {SizeText}"
        : $"{DisplayName}, {KindText}, {CategoryText}, {SizeText}";

    public bool IsDesktop => App.Kind == AppKind.Desktop;
    public AppKind Kind => App.Kind;
    /// <summary>Kind badge: "Десктопные" / "UWP".</summary>
    public string KindText => IsDesktop ? Strings.AppKind_Desktop : Strings.AppKind_Uwp;

    /// <summary>Name tooltip: publisher and version for a program, the package name for UWP.</summary>
    public string Details => IsDesktop
        ? string.Join(" · ", new[] { App.Publisher, App.Version }.Where(s => !string.IsNullOrWhiteSpace(s)).DefaultIfEmpty(App.DisplayName))
        : App.PackageFullName;

    public string SizeText => App.SizeText;
    public bool IsCritical => App.IsCritical;
    public AppCategory Category => App.Category;
    public string CategoryText => CategoryLabel(App.Category);

    /// <summary>Loaded off the UI thread after the row is shown (<see cref="AppsViewModel"/>).</summary>
    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value)) OnPropertyChanged(nameof(HasIcon));
        }
    }

    public bool HasIcon => _icon != null;

    /// <summary>First letter, used as a fallback tile when no icon is resolved.</summary>
    public string Initial => string.IsNullOrEmpty(App.DisplayName) ? "?" : App.DisplayName[..1].ToUpperInvariant();

    /// <summary>Protected UWP packages cannot be checked for removal; desktop programs always can.</summary>
    public bool CanRemove => !App.IsCritical;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (CanRemove) SetProperty(ref _isSelected, value); }
    }

    /// <summary>A desktop uninstaller is running for this row (its own window is open).</summary>
    public bool IsUninstalling
    {
        get => _isUninstalling;
        internal set => SetProperty(ref _isUninstalling, value);
    }

    /// <summary>Raised (false → true) when removing this app fails: the row shakes.</summary>
    public bool Failed
    {
        get => _failed;
        set => SetProperty(ref _failed, value);
    }

    /// <summary>Set true to trigger the SlideOut + FadeOut animation in the view.</summary>
    public bool IsRemoving
    {
        get => _isRemoving;
        set => SetProperty(ref _isRemoving, value);
    }

    /// <summary>Record the measured install size (arrives after the row is shown).</summary>
    internal void SetSize(long bytes)
    {
        if (bytes <= 0 || bytes == App.SizeBytes) return;
        App.SizeBytes = bytes;
        OnPropertyChanged(nameof(SizeText));
    }

    internal void SetIcon(ImageSource? icon) => Icon = icon;

    public static string CategoryLabel(AppCategory c) => c switch
    {
        AppCategory.Bloatware => Strings.AppCategory_Bloatware,
        AppCategory.Games => Strings.AppCategory_Games,
        AppCategory.Media => Strings.AppCategory_Media,
        AppCategory.Utilities => Strings.AppCategory_Utilities,
        AppCategory.ThirdParty => Strings.AppCategory_ThirdParty,
        AppCategory.System => Strings.AppCategory_System,
        AppCategory.Desktop => Strings.AppKind_Desktop,
        _ => Strings.AppCategory_Other,
    };
}

/// <summary>
/// The Apps page: every installed program in one list — desktop (Win32)
/// programs from the registry Uninstall keys and UWP packages — removed the
/// way each kind requires (its own uninstaller / Remove-AppxPackage).
/// </summary>
public sealed class AppsViewModel : ViewModelBase
{
    private readonly IUwpService _uwp;
    private readonly IDesktopAppService _desktop;
    private readonly INotificationService _notify;
    private bool _isLoading;
    private double _freedMb;
    /// <summary>Bumped by every load, so the icon/size pass for an older list stops.</summary>
    private int _loadGeneration;

    public AppsViewModel(IUwpService uwp, IDesktopAppService desktop, INotificationService notify)
    {
        _uwp = uwp;
        _desktop = desktop;
        _notify = notify;
        LoadCommand = new AsyncRelayCommand(_ => LoadAsync());
        SelectJunkCommand = new RelayCommand(SelectJunk);
        RemoveSelectedCommand = new AsyncRelayCommand(_ => RemoveSelectedAsync());
    }

    public ObservableCollection<AppItemViewModel> Apps { get; } = new();
    public AsyncRelayCommand LoadCommand { get; }
    public RelayCommand SelectJunkCommand { get; }
    public AsyncRelayCommand RemoveSelectedCommand { get; }

    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsLoaded => !IsLoading;

    /// <summary>Running total of megabytes freed by removals this session.</summary>
    public double FreedMb { get => _freedMb; private set => SetProperty(ref _freedMb, value); }
    public string FreedText => string.Format(CultureInfo.CurrentCulture, Strings.Apps_Freed, FreedMb);

    /// <summary>
    /// Both scans run in parallel off the UI thread (registry reads for desktop
    /// programs, PowerShell for UWP), each bounded by a timeout; the list shows
    /// when both are in, then icons and UWP sizes fill in row by row.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        OnPropertyChanged(nameof(IsLoaded));
        var generation = ++_loadGeneration;
        try
        {
            Apps.Clear();
            var desktopTask = TimeoutGuard.Await(
                _desktop.ListAsync(), TimeSpan.FromSeconds(60), Array.Empty<InstalledApp>(), "Apps.Desktop");
            var uwpTask = TimeoutGuard.Await(
                _uwp.ListAsync(), TimeSpan.FromSeconds(90), Array.Empty<InstalledApp>(), "Uwp.List");
            await Task.WhenAll(desktopTask, uwpTask);

            var desktop = await desktopTask;
            var uwp = await uwpTask;
            // Desktop programs first (what people usually come to remove), then UWP by category.
            ShowApps(desktop.Concat(uwp));
            _notify.Success(string.Format(CultureInfo.CurrentCulture, Strings.Apps_Found, Apps.Count, desktop.Count, uwp.Count));
            _ = FillInDetailsAsync(generation);
        }
        catch (Exception ex)
        {
            // Also started fire-and-forget on first navigation: never let it go unobserved.
            Logger.LogError("Apps.Load", ex);
            _notify.Error(string.Format(CultureInfo.CurrentCulture, Strings.App_UnhandledError, Logger.Describe(ex)));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsLoaded));
        }
    }

    /// <summary>
    /// After the rows are shown: icons for every row, then the size of each UWP
    /// package (walking its files takes seconds; desktop sizes come from the
    /// registry at once). One row at a time, off the UI thread; each await
    /// resumes on the UI thread to update the row. A reload abandons the pass.
    /// </summary>
    private async Task FillInDetailsAsync(int generation)
    {
        try
        {
            var rows = Apps.ToList();
            foreach (var item in rows)
            {
                if (generation != _loadGeneration) return;
                var app = item.App;
                item.SetIcon(await Task.Run(() => AppIconLoader.Load(app.IconPath, app.IconIndex)));
            }
            foreach (var item in rows.Where(r => !r.IsDesktop))
            {
                if (generation != _loadGeneration) return;
                item.SetSize(await _uwp.MeasureSizeAsync(item.App));
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget: never let a failure go unobserved.
            Logger.LogError("Apps.Details", ex);
        }
    }

    /// <summary>Replace the list. Also the design-time entry point (Design/DesignData).</summary>
    internal void ShowApps(IEnumerable<InstalledApp> apps)
    {
        Apps.Clear();
        foreach (var a in apps) Apps.Add(new AppItemViewModel(a));
    }

    /// <summary>Add to the session's "freed" counter.</summary>
    internal void AddFreed(long bytes)
    {
        FreedMb += bytes / 1024d / 1024d;
        OnPropertyChanged(nameof(FreedText));
    }

    /// <summary>
    /// "Smart" select: only preinstalled UWP junk (<see cref="AppCategory.Bloatware"/>).
    /// Desktop programs, protected packages, games, media, utilities and unknown
    /// packages stay unchecked.
    /// </summary>
    private void SelectJunk()
    {
        var count = 0;
        foreach (var a in Apps)
        {
            a.IsSelected = a.CanRemove && a.Category == AppCategory.Bloatware;
            if (a.IsSelected) count++;
        }
        _notify.Info(string.Format(CultureInfo.CurrentCulture, Strings.Apps_JunkSelected, count));
    }

    /// <summary>
    /// Remove the checked apps one after another: UWP packages silently, desktop
    /// programs through their own uninstaller window (the row shows that it is
    /// waiting). A program counts as removed only when its Uninstall entry is gone.
    /// </summary>
    private async Task RemoveSelectedAsync()
    {
        var selected = Apps.Where(a => a.IsSelected && a.CanRemove).ToList();
        if (selected.Count == 0)
        {
            _notify.Info(Strings.Apps_NothingSelected);
            return;
        }

        var removed = 0;
        foreach (var item in selected)
        {
            var (ok, freed) = item.IsDesktop ? await UninstallDesktopAsync(item) : await RemoveUwpAsync(item);
            if (!ok) continue;

            item.IsRemoving = true;              // start slide-out animation
            await Task.Delay(350);               // let the animation play
            Apps.Remove(item);
            removed++;
            AddFreed(freed);
        }

        var summary = string.Format(CultureInfo.CurrentCulture, Strings.Apps_RemoveDone, removed, selected.Count);
        if (removed == selected.Count) _notify.Success(summary);
        else _notify.Warning(summary);
    }

    private async Task<(bool Ok, long Freed)> RemoveUwpAsync(AppItemViewModel item)
    {
        var result = await _uwp.RemoveAsync(item.App);
        if (result.Success) return (true, result.FreedBytes);
        Fail(item, string.Format(CultureInfo.CurrentCulture, Strings.Apps_RemoveFailed, item.DisplayName));
        return (false, 0);
    }

    private async Task<(bool Ok, long Freed)> UninstallDesktopAsync(AppItemViewModel item)
    {
        item.IsUninstalling = true;
        UninstallOutcome outcome;
        try
        {
            outcome = await _desktop.UninstallAsync(item.App);
        }
        finally
        {
            item.IsUninstalling = false;
        }

        switch (outcome)
        {
            case UninstallOutcome.Removed:
                return (true, item.App.SizeBytes);
            case UninstallOutcome.StillInstalled:
                // Cancelled in the uninstaller, or it is still finishing: not an error.
                item.IsSelected = false;
                _notify.Info(string.Format(CultureInfo.CurrentCulture, Strings.Apps_StillInstalled, item.DisplayName));
                return (false, 0);
            default:
                Fail(item, string.Format(CultureInfo.CurrentCulture, Strings.Apps_UninstallNotStarted, item.DisplayName));
                return (false, 0);
        }
    }

    /// <summary>Keep the row (the app is still installed), uncheck and shake it.</summary>
    private void Fail(AppItemViewModel item, string message)
    {
        item.IsSelected = false;
        item.Failed = false;
        item.Failed = true;
        _notify.Error(message);
    }
}
