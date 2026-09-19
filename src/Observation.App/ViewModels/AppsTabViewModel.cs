using System.Collections.ObjectModel;
using Observation.App.Services;
using Observation.Handlers.Apps;

namespace Observation.App.ViewModels;

/// <summary>
/// «Установка программ»: компоненты Windows — обычные твики-тумблеры (из TweakLibrary,
/// как на любой другой вкладке), плюс каталог winget (см. план, «Установка программ —
/// техническая последовательность») — разовые установки, не тумблеры: программа либо уже
/// стоит, либо нет, откатывать через журнал нечем (winget сам решает, что уже установлено).
/// </summary>
public sealed class AppsTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<TweakGroupViewModel> Groups { get; }
    public IReadOnlyList<WingetCategoryViewModel> Categories { get; }
    public IReadOnlyList<WingetEntryViewModel> RuntimeBundle { get; }
    public ObservableCollection<string> Log { get; } = new();

    private readonly IWingetInstaller _installer;

    private bool? _wingetAvailable;
    public bool? WingetAvailable
    {
        get => _wingetAvailable;
        private set => SetField(ref _wingetAvailable, value);
    }

    public RelayCommand InstallCommand { get; }
    public RelayCommand InstallRuntimeBundleCommand { get; }

    public AppsTabViewModel(ILocalizationService localization, IReadOnlyList<TweakGroupViewModel> groups, IWingetInstaller installer)
    {
        Localization = localization;
        Groups = groups;
        _installer = installer;

        Categories = WingetCatalog.BuiltIn
            .Select(e => new WingetEntryViewModel(e))
            .GroupBy(e => e.Category)
            .Select(g => new WingetCategoryViewModel(g.Key, g.ToList()))
            .ToList();
        RuntimeBundle = WingetCatalog.RuntimeBundle.Select(e => new WingetEntryViewModel(e)).ToList();

        InstallCommand = new RelayCommand(async entry => await InstallAsync((WingetEntryViewModel)entry!));
        InstallRuntimeBundleCommand = new RelayCommand(async () => await InstallRuntimeBundleAsync(), () => WingetAvailable != false);

        _ = CheckWingetAsync();
    }

    private async Task CheckWingetAsync()
    {
        try
        {
            WingetAvailable = await _installer.IsAvailableAsync();
        }
        catch (Exception)
        {
            WingetAvailable = false;
        }

        InstallRuntimeBundleCommand.NotifyCanExecuteChanged();
        if (WingetAvailable == false)
            Log.Add("winget не найден — установите «Установщик приложений» из Microsoft Store, затем обновите вкладку.");
    }

    private async Task InstallAsync(WingetEntryViewModel entry)
    {
        entry.State = WingetInstallState.Installing;
        entry.Message = null;
        Log.Add($"Устанавливаю: {entry.Name} ({entry.Entry.PackageId})…");

        try
        {
            var result = await _installer.InstallAsync(entry.Entry.PackageId);
            if (result.Succeeded)
            {
                entry.State = WingetInstallState.Installed;
                Log.Add($"Готово: {entry.Name}");
            }
            else
            {
                entry.State = WingetInstallState.Failed;
                entry.Message = FirstLine(result.StandardError, result.StandardOutput);
                Log.Add($"Ошибка ({entry.Name}): {entry.Message}");
            }
        }
        catch (Exception ex)
        {
            entry.State = WingetInstallState.Failed;
            entry.Message = ex.Message;
            Log.Add($"Ошибка ({entry.Name}): {ex.Message}");
        }
    }

    private async Task InstallRuntimeBundleAsync()
    {
        // Последовательно, не параллельно — гонка за внутреннюю блокировку winget (см. план).
        foreach (var entry in RuntimeBundle)
            await InstallAsync(entry);
    }

    private static string FirstLine(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var trimmed = candidate.Trim();
            if (trimmed.Length > 0)
                return trimmed.Split('\n')[0];
        }

        return "неизвестная ошибка";
    }
}
