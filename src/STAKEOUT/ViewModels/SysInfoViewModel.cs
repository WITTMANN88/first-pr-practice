using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>Home page: reads and displays system information with a shimmer state.</summary>
public sealed class SysInfoViewModel : ViewModelBase
{
    private readonly SystemInfoService _service;
    private readonly DispatcherTimer _tempTimer;
    private bool _tempPollBusy;

    private bool _isLoading = true;
    private string _cpuName = "—";
    private double? _cpuTemp;
    private string _motherboard = "—";
    private string _ram = "—";
    private string _disk = "—";
    private string _windows = "—";

    public SysInfoViewModel(SystemInfoService service)
    {
        _service = service;
        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync());

        // Live temperature: re-poll only the (cheap) CPU temperature every 3 s so
        // the bar tracks reality without re-running the heavy WMI queries.
        _tempTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _tempTimer.Tick += async (_, _) => await PollTemperatureAsync();
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public ObservableCollection<GpuInfo> Gpus { get; } = new();

    /// <summary>True while WMI/LHM queries run; the view shows a shimmer skeleton.</summary>
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsLoaded => !IsLoading;

    public string CpuName { get => _cpuName; private set => SetProperty(ref _cpuName, value); }
    public string Motherboard { get => _motherboard; private set => SetProperty(ref _motherboard, value); }
    public string RamSummary { get => _ram; private set => SetProperty(ref _ram, value); }
    public string DiskSummary { get => _disk; private set => SetProperty(ref _disk, value); }
    public string WindowsVersion { get => _windows; private set => SetProperty(ref _windows, value); }

    /// <summary>CPU temperature in °C (0 when unavailable, for the progress bar).</summary>
    public double CpuTempValue => _cpuTemp ?? 0;
    public string CpuTempText => _cpuTemp.HasValue
        ? string.Format(CultureInfo.CurrentCulture, Strings.Unit_Celsius, _cpuTemp.Value)
        : Strings.Common_NotAvailable;
    /// <summary>Over 85 °C the temperature bar turns red.</summary>
    public bool TempIsHot => _cpuTemp is >= 85;

    /// <summary>Load a full snapshot. Only the temperature is cheap to re-poll.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        OnPropertyChanged(nameof(IsLoaded));
        try
        {
            // Cap the wait: even if a WMI provider hangs past its own timeout, the
            // UI is released with whatever data (or fallback) we have.
            var info = await TimeoutGuard.Await(
                _service.GatherAsync(), TimeSpan.FromSeconds(30), new SystemInfoModel(), "SysInfo.Load");

            CpuName = info.CpuName;
            _cpuTemp = info.CpuTemperatureC;
            Motherboard = info.Motherboard;
            RamSummary = info.RamSummary;
            DiskSummary = info.DiskSummary;
            WindowsVersion = info.WindowsVersion;

            Gpus.Clear();
            foreach (var g in info.Gpus) Gpus.Add(g);

            RaiseTempChanged();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsLoaded));
            _tempTimer.Start(); // begin live temperature polling after first load
        }
    }

    /// <summary>Re-poll just the CPU temperature (guarded, non-overlapping).</summary>
    private async Task PollTemperatureAsync()
    {
        if (_tempPollBusy) return;         // skip if a previous poll is still running
        _tempPollBusy = true;
        try
        {
            var t = await TimeoutGuard.Await(
                _service.RefreshTemperatureAsync(), TimeSpan.FromSeconds(6), _cpuTemp, "SysInfo.Temp");
            if (!Nullable.Equals(t, _cpuTemp))
            {
                _cpuTemp = t;
                RaiseTempChanged();
            }
        }
        finally
        {
            _tempPollBusy = false;
        }
    }

    /// <summary>Stop live polling (called when the app shuts down).</summary>
    public void StopLivePolling() => _tempTimer.Stop();

    private void RaiseTempChanged()
    {
        OnPropertyChanged(nameof(CpuTempValue));
        OnPropertyChanged(nameof(CpuTempText));
        OnPropertyChanged(nameof(TempIsHot));
    }
}
