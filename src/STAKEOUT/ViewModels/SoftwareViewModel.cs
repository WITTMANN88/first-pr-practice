using System.Collections.ObjectModel;
using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Mvvm;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>One software card with its own progress and morphing status text.</summary>
public sealed class SoftwareItemViewModel : ViewModelBase, IDisposable
{
    private readonly SoftwareInstallService _service;
    private readonly INotificationService _notify;
    private double _progress;
    private InstallStage _stage = InstallStage.Idle;
    private bool _busy;
    private bool _failed;

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

    /// <summary>Raised (false → true) on a failed install: the card shakes.</summary>
    public bool Failed { get => _failed; private set => SetProperty(ref _failed, value); }
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
        InstallStage.Background => Strings.Stage_Background,
        InstallStage.Blocked => Strings.Stage_Blocked,
        _ => ""
    };

    private static string F(string format, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>At exit: cancel an install still in progress. Safe while InstallAsync is
    /// awaiting: the source is cancelled first, and a second Dispose is a no-op.</summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

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
        Failed = false;
        Busy = true;
        OnPropertyChanged(nameof(IsIndeterminate));
        Progress = 0;
        _cts = new CancellationTokenSource();
        CancelCommand.NotifyCanExecuteChanged();
        var stageProgress = new Progress<InstallStage>(s => Stage = s);
        var barProgress = new Progress<double>(p => Progress = p);
        try
        {
            var outcome = await _service.InstallAsync(Item, stageProgress, barProgress, _cts.Token);
            switch (outcome)
            {
                case InstallOutcome.Started:
                    _notify.Success(F(Strings.Software_Started, DisplayName));
                    break;
                case InstallOutcome.Cancelled:
                    _notify.Info(F(Strings.Software_Cancelled, DisplayName));
                    break;
                case InstallOutcome.ContinuesInBackground:
                    _notify.Warning(F(Strings.Software_Detached, DisplayName));
                    break;
                case InstallOutcome.IntegrityFailure:
                    _notify.Error(F(Strings.Software_HashMismatch, DisplayName));
                    Failed = true;
                    break;
                case InstallOutcome.HashNotConfigured:
                    _notify.Error(F(Strings.Software_HashMissing, DisplayName));
                    Failed = true;
                    break;
                default:
                    _notify.Error(F(Strings.Software_Failed, DisplayName, StageText));
                    Failed = true;
                    break;
            }
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            Busy = false;
            OnPropertyChanged(nameof(IsIndeterminate));
            CancelCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>The software page: cards built from the install catalog.</summary>
public sealed class SoftwareViewModel : ViewModelBase, IDisposable
{
    public SoftwareViewModel(SoftwareInstallService service, INotificationService notify)
    {
        Items = new ObservableCollection<SoftwareItemViewModel>(
            service.Catalog.Select(i => new SoftwareItemViewModel(i, service, notify)));
    }

    public ObservableCollection<SoftwareItemViewModel> Items { get; }

    /// <summary>Called by the DI container at exit.</summary>
    public void Dispose()
    {
        foreach (var item in Items) item.Dispose();
    }
}
