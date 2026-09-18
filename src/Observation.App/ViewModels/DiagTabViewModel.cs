using System.Collections.ObjectModel;
using Observation.App.Services;
using Observation.Handlers.Diag;

namespace Observation.App.ViewModels;

/// <summary>
/// «Диагностика и драйверы»: тумблер классического меню F8 (из TweakLibrary) + список
/// проблемных устройств из реального скана Win32_PNPEntity (read-only, «искать в интернете»
/// вместо «исправить» — см. IProblemDeviceScanner) + быстрые ссылки на системные GUI-инструменты.
/// DPC/ISR-задержка из плана сюда не попала — честно нет уверенного способа вытащить
/// готовое число из трассировки wpr.exe без полного анализа в WPA (см. план, раздел «WHEA/ISR»).
/// </summary>
public sealed class DiagTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<TweakGroupViewModel> Groups { get; }
    public ObservableCollection<ProblemDeviceViewModel> Devices { get; } = new();

    private readonly IProblemDeviceScanner _scanner;

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set => SetField(ref _isScanning, value);
    }

    public bool IsEmpty => !IsScanning && Devices.Count == 0;

    public RelayCommand OpenDeviceManagerCommand { get; }
    public RelayCommand OpenSystemRestoreCommand { get; }
    public RelayCommand OpenControlPanelCommand { get; }
    public RelayCommand OpenSystemPropertiesCommand { get; }
    public RelayCommand OpenProgramsFeaturesCommand { get; }

    public DiagTabViewModel(ILocalizationService localization, IReadOnlyList<TweakGroupViewModel> groups, IProblemDeviceScanner scanner)
    {
        Localization = localization;
        Groups = groups;
        _scanner = scanner;

        OpenDeviceManagerCommand = new RelayCommand(() => ShellLauncher.Launch("devmgmt.msc"));
        OpenSystemRestoreCommand = new RelayCommand(() => ShellLauncher.Launch("rstrui.exe"));
        OpenControlPanelCommand = new RelayCommand(() => ShellLauncher.Launch("control.exe"));
        OpenSystemPropertiesCommand = new RelayCommand(() => ShellLauncher.Launch("sysdm.cpl"));
        OpenProgramsFeaturesCommand = new RelayCommand(() => ShellLauncher.Launch("control.exe", "appwiz.cpl"));

        _ = ScanAsync();
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
}
