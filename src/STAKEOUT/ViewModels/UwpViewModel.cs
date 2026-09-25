using System.Collections.ObjectModel;
using Stakeout.Core;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>One UWP package row with a checkbox and removal animation flag.</summary>
public sealed class UwpItemViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isRemoving;

    public UwpItemViewModel(UwpApp app) => App = app;

    public UwpApp App { get; }
    public string DisplayName => App.DisplayName;
    public string PackageFullName => App.PackageFullName;
    public string SizeText => App.SizeText;
    public bool IsCritical => App.IsCritical;
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

    /// <summary>Set true to trigger the SlideOut + FadeOut animation in the view.</summary>
    public bool IsRemoving
    {
        get => _isRemoving;
        set => SetProperty(ref _isRemoving, value);
    }
}

/// <summary>The UWP removal page.</summary>
public sealed class UwpViewModel : ViewModelBase
{
    private readonly UwpService _service;
    private readonly ToastService _toast;
    private bool _isLoading;
    private double _freedMb;

    public UwpViewModel(UwpService service, ToastService toast)
    {
        _service = service;
        _toast = toast;
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
    public string FreedText => $"Освобождено: {FreedMb:0.#} МБ";

    public async Task LoadAsync()
    {
        IsLoading = true;
        OnPropertyChanged(nameof(IsLoaded));
        try
        {
            Apps.Clear();
            // Guard the PowerShell enumeration so a hung host releases the UI.
            var list = await TimeoutGuard.Await(
                _service.ListAsync(), TimeSpan.FromSeconds(90), new(), "Uwp.List");
            foreach (var a in list) Apps.Add(new UwpItemViewModel(a));
            _toast.Success($"Найдено приложений: {Apps.Count}");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsLoaded));
        }
    }

    /// <summary>"Умное" выделение: только мусор, критические приложения игнорируются.</summary>
    private void SelectJunk()
    {
        foreach (var a in Apps)
            a.IsSelected = a.CanRemove;
    }

    private async Task RemoveSelectedAsync()
    {
        var selected = Apps.Where(a => a.IsSelected && a.CanRemove).ToList();
        if (selected.Count == 0)
        {
            _toast.Show("Ничего не выбрано");
            return;
        }

        foreach (var item in selected)
        {
            var freedBytes = await _service.RemoveAsync(item.App);
            if (freedBytes >= 0 && !item.App.IsCritical)
            {
                item.IsRemoving = true;              // start slide-out animation
                await Task.Delay(350);               // let the animation play
                Apps.Remove(item);
                FreedMb += freedBytes / 1024d / 1024d;
                OnPropertyChanged(nameof(FreedText));
            }
        }
        _toast.Success("Удаление завершено");
    }
}
