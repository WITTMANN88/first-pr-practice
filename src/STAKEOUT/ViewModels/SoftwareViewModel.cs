using System.Collections.ObjectModel;
using Stakeout.Core;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>One software card with its own progress and morphing status text.</summary>
public sealed class SoftwareItemViewModel : ViewModelBase
{
    private readonly SoftwareInstallService _service;
    private readonly ToastService _toast;
    private double _progress;
    private InstallStage _stage = InstallStage.Idle;
    private bool _busy;

    public SoftwareItemViewModel(SoftwareItem item, SoftwareInstallService service, ToastService toast)
    {
        Item = item;
        _service = service;
        _toast = toast;
        InstallCommand = new AsyncRelayCommand(_ => InstallAsync(), _ => !Busy);
    }

    public SoftwareItem Item { get; }
    public string DisplayName => Item.DisplayName;
    public string MethodText => Item.Method == InstallMethod.Winget ? "winget" : "прямая ссылка";
    public AsyncRelayCommand InstallCommand { get; }

    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    /// <summary>Winget installs have no byte progress, so the bar runs indeterminate.</summary>
    public bool IsIndeterminate => Busy && Item.Method == InstallMethod.Winget;

    public InstallStage Stage
    {
        get => _stage;
        private set { if (SetProperty(ref _stage, value)) OnPropertyChanged(nameof(StageText)); }
    }

    public string StageText => Stage switch
    {
        InstallStage.Idle => "",
        InstallStage.Downloading => "Скачивание...",
        InstallStage.Extracting => "Распаковка...",
        InstallStage.Installing => "Установка...",
        InstallStage.Done => "Готово",
        InstallStage.Failed => Item.NeedsUrlConfig ? "Ссылка не настроена" : "Ошибка",
        _ => ""
    };

    private async Task InstallAsync()
    {
        Busy = true;
        OnPropertyChanged(nameof(IsIndeterminate));
        Progress = 0;
        var stageProgress = new Progress<InstallStage>(s => Stage = s);
        var barProgress = new Progress<double>(p => Progress = p);
        try
        {
            var ok = await _service.InstallAsync(Item, stageProgress, barProgress);
            if (ok) _toast.Success($"{DisplayName}: установка запущена");
            else _toast.Error($"{DisplayName}: {StageText}");
        }
        finally
        {
            Busy = false;
            OnPropertyChanged(nameof(IsIndeterminate));
        }
    }
}

/// <summary>The software page: cards built from the install catalog.</summary>
public sealed class SoftwareViewModel : ViewModelBase
{
    public SoftwareViewModel(SoftwareInstallService service, ToastService toast)
    {
        Items = new ObservableCollection<SoftwareItemViewModel>(
            service.Catalog.Select(i => new SoftwareItemViewModel(i, service, toast)));
    }

    public ObservableCollection<SoftwareItemViewModel> Items { get; }
}
