using System.Collections.ObjectModel;
using Observation.App.Services;
using Observation.Handlers.Diag;

namespace Observation.App.ViewModels;

/// <summary>
/// «Диагностика и драйверы»: тумблер классического меню F8 (из TweakLibrary) + список
/// проблемных устройств из реального скана Win32_PNPEntity (read-only, «искать в интернете»
/// вместо «исправить» — см. IProblemDeviceScanner) + быстрые ссылки на системные GUI-инструменты
/// + текущая загрузка DPC/прерываний (честная замена «задержки в мкс», см.
/// PowerShellDpcActivitySampler) + версия драйвера видеокарты со ссылкой на страницу вендора.
/// </summary>
public sealed class DiagTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<TweakGroupViewModel> Groups { get; }
    public ObservableCollection<ProblemDeviceViewModel> Devices { get; } = new();
    public ObservableCollection<GpuDriverViewModel> GpuDrivers { get; } = new();

    private readonly IProblemDeviceScanner _scanner;
    private readonly IDpcActivitySampler _dpcSampler;
    private readonly IGpuDriverInfoProvider _gpuDriverProvider;

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set => SetField(ref _isScanning, value);
    }

    public bool IsEmpty => !IsScanning && Devices.Count == 0;

    private double _dpcPercent;
    public double DpcPercent
    {
        get => _dpcPercent;
        private set => SetField(ref _dpcPercent, value);
    }

    private double _interruptPercent;
    public double InterruptPercent
    {
        get => _interruptPercent;
        private set => SetField(ref _interruptPercent, value);
    }

    private bool _isDpcAvailable;
    public bool IsDpcAvailable
    {
        get => _isDpcAvailable;
        private set => SetField(ref _isDpcAvailable, value);
    }

    public RelayCommand OpenDeviceManagerCommand { get; }
    public RelayCommand OpenSystemRestoreCommand { get; }
    public RelayCommand OpenControlPanelCommand { get; }
    public RelayCommand OpenSystemPropertiesCommand { get; }
    public RelayCommand OpenProgramsFeaturesCommand { get; }

    public DiagTabViewModel(ILocalizationService localization, IReadOnlyList<TweakGroupViewModel> groups, IProblemDeviceScanner scanner,
        IDpcActivitySampler dpcSampler, IGpuDriverInfoProvider gpuDriverProvider)
    {
        Localization = localization;
        Groups = groups;
        _scanner = scanner;
        _dpcSampler = dpcSampler;
        _gpuDriverProvider = gpuDriverProvider;

        OpenDeviceManagerCommand = new RelayCommand(() => ShellLauncher.Launch("devmgmt.msc"));
        OpenSystemRestoreCommand = new RelayCommand(() => ShellLauncher.Launch("rstrui.exe"));
        OpenControlPanelCommand = new RelayCommand(() => ShellLauncher.Launch("control.exe"));
        OpenSystemPropertiesCommand = new RelayCommand(() => ShellLauncher.Launch("sysdm.cpl"));
        OpenProgramsFeaturesCommand = new RelayCommand(() => ShellLauncher.Launch("control.exe", "appwiz.cpl"));

        _ = ScanAsync();
        _ = SampleDpcAsync();
        _ = LoadGpuDriversAsync();
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var devices = await _scanner.ScanAsync();
            Devices.Clear();
            foreach (var device in devices.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                Devices.Add(new ProblemDeviceViewModel(device));
        }
        catch (Exception)
        {
            // Честно оставляем список пустым — тот же принцип, что и в DebloatTabViewModel.ScanAsync,
            // но здесь нет отдельного поля-сообщения об ошибке: список проблемных устройств
            // второстепенен, а не главное действие вкладки.
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task SampleDpcAsync()
    {
        try
        {
            var sample = await _dpcSampler.SampleAsync();
            DpcPercent = sample.DpcPercent;
            InterruptPercent = sample.InterruptPercent;
            IsDpcAvailable = true;
        }
        catch (Exception)
        {
            IsDpcAvailable = false;
        }
    }

    private async Task LoadGpuDriversAsync()
    {
        try
        {
            var drivers = await _gpuDriverProvider.GetAsync();
            GpuDrivers.Clear();
            foreach (var driver in drivers)
                GpuDrivers.Add(new GpuDriverViewModel(driver));
        }
        catch (Exception)
        {
            // Честно оставляем список пустым — второстепенная информационная карточка.
        }
    }
}
