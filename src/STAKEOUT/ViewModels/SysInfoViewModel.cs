using System.Collections.ObjectModel;
using Stakeout.Core;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>Home page: reads and displays system information with a shimmer state.</summary>
public sealed class SysInfoViewModel : ViewModelBase
{
    private readonly SystemInfoService _service;

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
    public string CpuTempText => _cpuTemp.HasValue ? $"{_cpuTemp.Value:0.#} °C" : "н/д";
    /// <summary>Over 85 °C the temperature bar turns red.</summary>
    public bool TempIsHot => _cpuTemp is >= 85;

    /// <summary>Load a full snapshot. Only the temperature is cheap to re-poll.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        OnPropertyChanged(nameof(IsLoaded));
        try
        {
            var info = await _service.GatherAsync();
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
        }
    }

    private void RaiseTempChanged()
    {
        OnPropertyChanged(nameof(CpuTempValue));
        OnPropertyChanged(nameof(CpuTempText));
        OnPropertyChanged(nameof(TempIsHot));
    }
}
