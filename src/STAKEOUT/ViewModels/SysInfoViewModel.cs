using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>
/// Home page: system information with a shimmer state, a live temperature bar
/// and a ~2 minute temperature sparkline.
/// </summary>
public sealed class SysInfoViewModel : ViewModelBase
{
    /// <summary>Sparkline plot area (device-independent px). Fits the 360 px CPU card
    /// with room on the right for the reference-line label.</summary>
    public const double TempPlotWidth = 276;
    public const double TempPlotHeight = 48;
    public const double TempLimitLabelLeft = TempPlotWidth + 8;

    private readonly SystemInfoService _service;
    private readonly DispatcherTimer _tempTimer;
    private readonly SampleHistory _tempHistory = new(CpuTemperatureScale.HistoryLength);
    private bool _tempPollBusy;
    private PointCollection _tempPoints = Frozen(new PointCollection());
    private double _tempLimitY;
    private Point _tempNow;

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
        _tempTimer = new DispatcherTimer { Interval = CpuTemperatureScale.PollInterval };
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
    /// <summary>At the overheat threshold the temperature bar turns red.</summary>
    public bool TempIsHot => CpuTemperatureScale.IsHot(_cpuTemp);

    // --- sparkline -----------------------------------------------------------
    // Geometry is computed here (pure SampleHistory math) so the view only
    // places shapes; the collection is frozen and replaced, never mutated.

    public bool HasTempHistory => _tempHistory.Count > 0;
    /// <summary>Polyline points, oldest→newest, newest at the right edge.</summary>
    public PointCollection TempHistoryPoints => _tempPoints;
    /// <summary>Y of the overheat reference line.</summary>
    public double TempLimitY => _tempLimitY;
    /// <summary>Top of the reference-line label, vertically centred on the line.</summary>
    public double TempLimitLabelTop => _tempLimitY - 7;
    public string TempLimitText => F(Strings.SysInfo_TempLimit, CpuTemperatureScale.HotThresholdC);
    /// <summary>Top-left of the 12 px end-point marker (centred on the newest sample).</summary>
    public double TempNowLeft => _tempNow.X - 6;
    public double TempNowTop => _tempNow.Y - 6;
    /// <summary>Min / max as text: the chart itself carries no numbers.</summary>
    public string TempRangeText => HasTempHistory
        ? F(Strings.SysInfo_TempRange, _tempHistory.Min!.Value, _tempHistory.Max!.Value)
        : Strings.SysInfo_TempNoSensor;
    /// <summary>Tooltip and screen-reader description of the whole chart.</summary>
    public string TempSummary => HasTempHistory
        ? F(Strings.SysInfo_TempSummary, _tempHistory.Count, _tempHistory.Latest!.Value,
            _tempHistory.Min!.Value, _tempHistory.Max!.Value, CpuTemperatureScale.HotThresholdC)
        : Strings.SysInfo_TempNoSensor;

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
            ApplySnapshot(info);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsLoaded));
            _tempTimer.Start(); // begin live temperature polling after first load
        }
    }

    /// <summary>
    /// Show a snapshot. Also the design-time entry point (Design/DesignData), which
    /// fills the page without touching WMI or sensors.
    /// </summary>
    internal void ApplySnapshot(SystemInfoModel info)
    {
        CpuName = info.CpuName;
        Motherboard = info.Motherboard;
        RamSummary = info.RamSummary;
        DiskSummary = info.DiskSummary;
        WindowsVersion = info.WindowsVersion;

        Gpus.Clear();
        foreach (var g in info.Gpus) Gpus.Add(g);

        RecordTemperature(info.CpuTemperatureC);
        IsLoading = false;
        OnPropertyChanged(nameof(IsLoaded));
    }

    /// <summary>
    /// Record one temperature reading: updates the bar and appends to the sparkline.
    /// Every poll is a sample (a flat line is information too); a missing reading
    /// (null) is skipped, not drawn as zero.
    /// </summary>
    internal void RecordTemperature(double? celsius)
    {
        _cpuTemp = celsius;
        if (celsius.HasValue) _tempHistory.Add(celsius.Value);
        RaiseTempChanged();
        RebuildSparkline();
    }

    /// <summary>Re-poll just the CPU temperature (guarded, non-overlapping).</summary>
    private async Task PollTemperatureAsync()
    {
        if (_tempPollBusy) return;         // skip if a previous poll is still running
        _tempPollBusy = true;
        try
        {
            // NaN marks a timed-out poll: keep the last value on the bar and add no
            // sample, rather than drawing a reading that was never taken. A null
            // result is real (sensor gone) and shows as "n/a".
            var t = await TimeoutGuard.Await(
                _service.RefreshTemperatureAsync(), TimeSpan.FromSeconds(6), (double?)double.NaN, "SysInfo.Temp");
            if (t is double v && double.IsNaN(v)) return;
            RecordTemperature(t);
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

    private void RebuildSparkline()
    {
        var (min, max) = _tempHistory.Domain(CpuTemperatureScale.DomainFloorC, CpuTemperatureScale.DomainCeilingC);
        var points = new PointCollection(_tempHistory.Count);
        foreach (var (x, y) in _tempHistory.ToPoints(TempPlotWidth, TempPlotHeight, min, max))
            points.Add(new Point(x, y));

        _tempPoints = Frozen(points);
        _tempLimitY = SampleHistory.ScaleY(CpuTemperatureScale.HotThresholdC, TempPlotHeight, min, max);
        _tempNow = points.Count > 0 ? points[^1] : new Point(TempPlotWidth, TempPlotHeight);

        OnPropertyChanged(nameof(HasTempHistory));
        OnPropertyChanged(nameof(TempHistoryPoints));
        OnPropertyChanged(nameof(TempLimitY));
        OnPropertyChanged(nameof(TempLimitLabelTop));
        OnPropertyChanged(nameof(TempNowLeft));
        OnPropertyChanged(nameof(TempNowTop));
        OnPropertyChanged(nameof(TempRangeText));
        OnPropertyChanged(nameof(TempSummary));
    }

    /// <summary>Frozen freezables are read-only and cheaper to render and bind.</summary>
    private static PointCollection Frozen(PointCollection p)
    {
        p.Freeze();
        return p;
    }

    private static string F(string format, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, format, args);
}
