using System.Collections.ObjectModel;
using Observation.App.Services;
using Observation.Handlers.Debloat;

namespace Observation.App.ViewModels;

/// <summary>
/// «Деблоат и UWP»: обычные твики-тумблеры (Brave/Edge/OneDrive, из TweakLibrary, как на
/// любой другой вкладке) плюс реальный скан установленных UWP-пакетов конкретной машины
/// (см. план, «Сканирование UWP-приложений») — список динамический, живёт не в TweakLibrary,
/// а здесь; удаление — сразу, а не через накопительную очередь «Применить» (пакет либо
/// удалён, либо нет, откатывать через журнал best-effort всё равно нечем).
/// </summary>
public sealed class DebloatTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<TweakGroupViewModel> Groups { get; }
    public ObservableCollection<UwpPackageViewModel> UwpPackages { get; } = new();

    private readonly IUwpPackageScanner _scanner;

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set => SetField(ref _isScanning, value);
    }

    private string? _scanMessage;
    public string? ScanMessage
    {
        get => _scanMessage;
        private set => SetField(ref _scanMessage, value);
    }

    public RelayCommand ScanCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public DebloatTabViewModel(ILocalizationService localization, IReadOnlyList<TweakGroupViewModel> groups, IUwpPackageScanner scanner)
    {
        Localization = localization;
        Groups = groups;
        _scanner = scanner;

        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning);
        RemoveCommand = new RelayCommand(async pkg => await RemoveAsync((UwpPackageViewModel)pkg!), _ => !IsScanning);
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        ScanCommand.NotifyCanExecuteChanged();
        ScanMessage = null;

        try
        {
            var packages = await _scanner.ScanAsync();
            UwpPackages.Clear();
            foreach (var package in packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                UwpPackages.Add(new UwpPackageViewModel(package));

            ScanMessage = $"Найдено пакетов: {UwpPackages.Count}";
        }
        catch (Exception ex)
        {
            ScanMessage = $"Не удалось просканировать: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ScanCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task RemoveAsync(UwpPackageViewModel package)
    {
        package.IsRemoving = true;
        package.StatusMessage = null;

        try
        {
            var result = await _scanner.RemoveAsync(package.Info.PackageFullName);
            if (result.Succeeded)
                UwpPackages.Remove(package);
            else
                package.StatusMessage = $"Ошибка: {FirstLine(result.StandardError)}";
        }
        catch (Exception ex)
        {
            package.StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            package.IsRemoving = false;
        }
    }

    private static string FirstLine(string text) =>
        string.IsNullOrWhiteSpace(text) ? "неизвестная ошибка" : text.Trim().Split('\n')[0];
}
