using System.Windows;
using Observation.App.Runtime;
using Observation.App.Services;
using Observation.Core.Batch;
using Observation.Core.Conflicts;
using Observation.Core.SystemAccess;

namespace Observation.App.ViewModels;

/// <summary>
/// «Главная»: очередь изменённых твиков (из TweakLibrary, по всем вкладкам) и кнопка
/// «Применить» — реальный проход через ConflictDetector и BatchRunner, не макет.
/// Пресеты/журнал/статистика системы из ui-preview.html сюда ещё не переехали —
/// это отдельный, более поздний проход стилизации, не эта задача (подключить реальное применение).
/// </summary>
public sealed class HomeTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }

    private readonly TweakLibrary _library;
    private readonly BatchRunner _batchRunner;
    private readonly ConflictDetector _conflictDetector;
    private readonly SystemContext _systemContext;

    public IReadOnlyList<TweakItemViewModel> PendingChanges => _library.PendingChanges;
    public int PendingCount => PendingChanges.Count;

    private bool _isApplying;
    public bool IsApplying
    {
        get => _isApplying;
        private set => SetField(ref _isApplying, value);
    }

    private string? _resultMessage;
    public string? ResultMessage
    {
        get => _resultMessage;
        private set => SetField(ref _resultMessage, value);
    }

    private bool _resultHasFailures;
    public bool ResultHasFailures
    {
        get => _resultHasFailures;
        private set => SetField(ref _resultHasFailures, value);
    }

    public RelayCommand ApplyCommand { get; }

    public HomeTabViewModel(ILocalizationService localization, TweakLibrary library, BatchRunner batchRunner,
        ConflictDetector conflictDetector, SystemContext systemContext)
    {
        Localization = localization;
        _library = library;
        _batchRunner = batchRunner;
        _conflictDetector = conflictDetector;
        _systemContext = systemContext;

        ApplyCommand = new RelayCommand(async () => await ApplyAsync(), () => !IsApplying && PendingCount > 0);
        _library.PendingChanged += OnPendingChanged;
    }

    private void OnPendingChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PendingChanges));
        OnPropertyChanged(nameof(PendingCount));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyAsync()
    {
        var pendingIds = PendingChanges.Select(i => i.Definition.Id).ToList();
        var conflicts = _conflictDetector.Scan(pendingIds);
        if (conflicts.Count > 0)
        {
            var text = string.Join("\n", conflicts.Select(c => $"• {c.MessageRu}"));
            var proceed = MessageBox.Show(
                $"Обнаружены конфликты в выбранных твиках:\n\n{text}\n\nПродолжить применение?",
                "Конфликт твиков", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (proceed != MessageBoxResult.Yes)
                return;
        }

        IsApplying = true;
        ApplyCommand.NotifyCanExecuteChanged();
        ResultMessage = null;
        ResultHasFailures = false;

        try
        {
            var result = await _library.ApplyPendingAsync(_batchRunner, _systemContext);
            var failed = result.Outcomes.Count(o => !o.Success);
            ResultHasFailures = failed > 0;
            ResultMessage = failed == 0
                ? $"Применено успешно: {result.Outcomes.Count}"
                : $"Применено: {result.Outcomes.Count - failed} из {result.Outcomes.Count}, ошибок: {failed}";
        }
        finally
        {
            IsApplying = false;
            OnPendingChanged(this, EventArgs.Empty);
        }
    }
}
