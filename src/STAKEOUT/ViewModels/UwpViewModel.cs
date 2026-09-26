using System.Collections.ObjectModel;
using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>One UWP package row with a checkbox, category badge and removal animation flag.</summary>
public sealed class UwpItemViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isRemoving;
    private bool _failed;

    public UwpItemViewModel(UwpApp app) => App = app;

    /// <summary>Record the measured install size (arrives after the row is shown).</summary>
    internal void SetSize(long bytes)
    {
        if (bytes <= 0 || bytes == App.SizeBytes) return;
        App.SizeBytes = bytes;
        OnPropertyChanged(nameof(SizeText));
    }

    public UwpApp App { get; }
    public string DisplayName => App.DisplayName;
    public string PackageFullName => App.PackageFullName;
    public string SizeText => App.SizeText;
    public bool IsCritical => App.IsCritical;
    public UwpCategory Category => App.Category;
    public string CategoryText => CategoryLabel(App.Category);
    public string? IconPath => App.IconPath;
    public bool HasIcon => !string.IsNullOrEmpty(App.IconPath);
    /// <summary>First letter, used as a fallback tile when no icon is resolved.</summary>
    public string Initial => string.IsNullOrEmpty(App.DisplayName) ? "?" : App.DisplayName[..1].ToUpperInvariant();

    /// <summary>Protected apps cannot be checked for removal.</summary>
    public bool CanRemove => !App.IsCritical;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (CanRemove) SetProperty(ref _isSelected, value); }
    }

    /// <summary>Raised (false → true) when removing this package fails: the row shakes.</summary>
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

    public static string CategoryLabel(UwpCategory c) => c switch
    {
        UwpCategory.Bloatware => Strings.UwpCategory_Bloatware,
        UwpCategory.Games => Strings.UwpCategory_Games,
        UwpCategory.Media => Strings.UwpCategory_Media,
        UwpCategory.Utilities => Strings.UwpCategory_Utilities,
        UwpCategory.ThirdParty => Strings.UwpCategory_ThirdParty,
        UwpCategory.System => Strings.UwpCategory_System,
        _ => Strings.UwpCategory_Other,
    };
}

/// <summary>The UWP removal page.</summary>
public sealed class UwpViewModel : ViewModelBase
{
    private readonly IUwpService _service;
    private readonly INotificationService _notify;
    private bool _isLoading;
    private double _freedMb;
    /// <summary>Bumped by every load, so a size pass for an older list stops.</summary>
    private int _loadGeneration;

    public UwpViewModel(IUwpService service, INotificationService notify)
    {
        _service = service;
        _notify = notify;
        LoadCommand = new AsyncRelayCommand(_ => LoadAsync());
        SelectJunkCommand = new RelayCommand(SelectJunk);
        RemoveSelectedCommand = new AsyncRelayCommand(_ => RemoveSelectedAsync());
    }

    public ObservableCollection<UwpItemViewModel> Apps { get; } = new();
    public AsyncRelayCommand LoadCommand { get; }
    public RelayCommand SelectJunkCommand { get; }
    public AsyncRelayCommand RemoveSelectedCommand { get; }

    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsLoaded => !IsLoading;

    /// <summary>Running total of megabytes freed by removals this session.</summary>
    public double FreedMb { get => _freedMb; private set => SetProperty(ref _freedMb, value); }
    public string FreedText => string.Format(CultureInfo.CurrentCulture, Strings.Uwp_Freed, FreedMb);

    public async Task LoadAsync()
    {
        IsLoading = true;
        OnPropertyChanged(nameof(IsLoaded));
        var generation = ++_loadGeneration;
        try
        {
            Apps.Clear();
            // Guard the PowerShell enumeration so a hung host releases the UI.
            var list = await TimeoutGuard.Await(
                _service.ListAsync(), TimeSpan.FromSeconds(90), Array.Empty<UwpApp>(), "Uwp.List");
            ShowApps(list);
            _notify.Success(string.Format(CultureInfo.CurrentCulture, Strings.Uwp_Found, Apps.Count));
            _ = MeasureSizesAsync(generation);
        }
        catch (Exception ex)
        {
            // Also started fire-and-forget on first navigation: never let it go unobserved.
            Logger.LogError("Uwp.Load", ex);
            _notify.Error(string.Format(CultureInfo.CurrentCulture, Strings.App_UnhandledError, ex.Message));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsLoaded));
        }
    }

    /// <summary>
    /// Fill in sizes after the rows are shown: walking every package's files takes
    /// seconds, so the list appears at once and sizes arrive one by one, on the
    /// UI thread (each await resumes there). A reload abandons the older pass.
    /// </summary>
    private async Task MeasureSizesAsync(int generation)
    {
        try
        {
            foreach (var item in Apps.ToList())
            {
                if (generation != _loadGeneration) return;
                item.SetSize(await _service.MeasureSizeAsync(item.App));
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget: never let a failure go unobserved.
            Logger.LogError("Uwp.Size", ex);
        }
    }

    /// <summary>Replace the list. Also the design-time entry point (Design/DesignData).</summary>
    internal void ShowApps(IEnumerable<UwpApp> apps)
    {
        Apps.Clear();
        foreach (var a in apps) Apps.Add(new UwpItemViewModel(a));
    }

    /// <summary>Add to the session's "freed" counter.</summary>
    internal void AddFreed(long bytes)
    {
        FreedMb += bytes / 1024d / 1024d;
        OnPropertyChanged(nameof(FreedText));
    }

    /// <summary>
    /// "Smart" select: only preinstalled junk (<see cref="UwpCategory.Bloatware"/>).
    /// Protected apps, games, media, utilities and unknown packages stay unchecked.
    /// </summary>
    private void SelectJunk()
    {
        var count = 0;
        foreach (var a in Apps)
        {
            a.IsSelected = a.CanRemove && a.Category == UwpCategory.Bloatware;
            if (a.IsSelected) count++;
        }
        _notify.Info(string.Format(CultureInfo.CurrentCulture, Strings.Uwp_JunkSelected, count));
    }

    private async Task RemoveSelectedAsync()
    {
        var selected = Apps.Where(a => a.IsSelected && a.CanRemove).ToList();
        if (selected.Count == 0)
        {
            _notify.Info(Strings.Uwp_NothingSelected);
            return;
        }

        var removed = 0;
        foreach (var item in selected)
        {
            var result = await _service.RemoveAsync(item.App);
            if (!result.Success)
            {
                // Keep the row (the package is still installed) and shake it.
                item.IsSelected = false;
                item.Failed = false;
                item.Failed = true;
                _notify.Error(string.Format(CultureInfo.CurrentCulture, Strings.Uwp_RemoveFailed, item.DisplayName));
                continue;
            }

            item.IsRemoving = true;              // start slide-out animation
            await Task.Delay(350);               // let the animation play
            Apps.Remove(item);
            removed++;
            AddFreed(result.FreedBytes);
        }

        var summary = string.Format(CultureInfo.CurrentCulture, Strings.Uwp_RemoveDone, removed, selected.Count);
        if (removed == selected.Count) _notify.Success(summary);
        else _notify.Warning(summary);
    }
}
