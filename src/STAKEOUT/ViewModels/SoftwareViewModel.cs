using System.Collections.ObjectModel;
using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>One software card with its own progress and morphing status text.</summary>
public sealed class SoftwareItemViewModel : ViewModelBase
{
    private readonly SoftwareInstallService _service;
    private readonly INotificationService _notify;
    private double _progress;
    private InstallStage _stage = InstallStage.Idle;
    private bool _busy;

    private CancellationTokenSource? _cts;

    public SoftwareItemViewModel(SoftwareItem item, SoftwareInstallService service, INotificationService notify)
    {
        Item = item;
        _service = service;
        _notify = notify;
        InstallCommand = new AsyncRelayCommand(_ => InstallAsync(), _ => !Busy);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => Busy);
    }

    public SoftwareItem Item { get; }
    public string DisplayName => Item.DisplayName;
    /// <summary>Key of the vector logo DrawingImage in Themes/Logos.xaml.</summary>
    public string LogoKey => "Logo." + Item.Key;
    public string MethodText => Item.Method == InstallMethod.Winget
        ? Strings.Software_MethodWinget
        : Strings.Software_MethodDirect;
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand CancelCommand { get; }

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
        InstallStage.Downloading => Strings.Stage_Downloading,
        InstallStage.Extracting => Strings.Stage_Extracting,
        InstallStage.Installing => Strings.Stage_Installing,
        InstallStage.Done => Strings.Stage_Done,
        InstallStage.Failed => Item.NeedsUrlConfig ? Strings.Stage_UrlMissing : Strings.Stage_Failed,
        _ => ""
    };

    private static string F(string format, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>Freeze the card mid-install. Design-time entry point (Design/DesignData).</summary>
    internal void ShowProgress(InstallStage stage, double progress)
    {
        Busy = true;
        Stage = stage;
        Progress = progress;
        OnPropertyChanged(nameof(IsIndeterminate));
    }

    private async Task InstallAsync()
    {
        Busy = true;
        OnPropertyChanged(nameof(IsIndeterminate));
        Progress = 0;
        _cts = new CancellationTokenSource();
        CancelCommand.RaiseCanExecuteChanged();
        var stageProgress = new Progress<InstallStage>(s => Stage = s);
        var barProgress = new Progress<double>(p => Progress = p);
        try
        {
            var ok = await _service.InstallAsync(Item, stageProgress, barProgress, _cts.Token);
            if (ok) _notify.Success(F(Strings.Software_Started, DisplayName));
            else if (_cts.IsCancellationRequested) _notify.Info(F(Strings.Software_Cancelled, DisplayName));
            else _notify.Error(F(Strings.Software_Failed, DisplayName, StageText));
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            Busy = false;
            OnPropertyChanged(nameof(IsIndeterminate));
            CancelCommand.RaiseCanExecuteChanged();
        }
    }
}

/// <summary>The software page: cards built from the install catalog.</summary>
public sealed class SoftwareViewModel : ViewModelBase
{
    public SoftwareViewModel(SoftwareInstallService service, INotificationService notify)
    {
        Items = new ObservableCollection<SoftwareItemViewModel>(
            service.Catalog.Select(i => new SoftwareItemViewModel(i, service, notify)));
    }

    public ObservableCollection<SoftwareItemViewModel> Items { get; }
}
