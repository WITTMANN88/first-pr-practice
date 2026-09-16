using System.Windows;
using Observation.App.Runtime;
using Observation.App.Services;
using Observation.Core.Batch;
using Observation.Core.Conflicts;
using Observation.Core.Engine;
using Observation.Core.Journal;
using Observation.Core.SystemAccess;

namespace Observation.App.ViewModels;

/// <summary>
/// «Главная»: очередь изменённых твиков (из TweakLibrary, по всем вкладкам), «Применить»
/// через ConflictDetector+BatchRunner, «активные твики» из журнала — «Проверить состояние»
/// (мех. надёжности №1) и точечный откат конкретного твика к состоянию «до», пресеты (только
/// выставляют тумблеры, не применяют), карточка характеристик ПК и панель последних записей
/// журнала (история, отдельно от «активных твиков» — там только restore-previous/additive-tagged).
/// </summary>
public sealed class HomeTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }

    private readonly TweakLibrary _library;
    private readonly TweakEngine _engine;
    private readonly BatchRunner _batchRunner;
    private readonly ConflictDetector _conflictDetector;
    private readonly SystemContext _systemContext;
    private readonly IJournalStore _journal;

    public IReadOnlyList<TweakItemViewModel> PendingChanges => _library.PendingChanges;
    public int PendingCount => PendingChanges.Count;

    public IReadOnlyList<TweakItemViewModel> ActiveTweaks => _library.GetRevertibleTweaks();
    public IReadOnlyList<PresetViewModel> Presets { get; }
    public PcSpecs PcSpecs { get; }

    public IReadOnlyList<JournalEntryDisplay> RecentJournalEntries =>
        _journal.GetRecentEntries(20)
            .Select(e => new JournalEntryDisplay(
                _library.ItemsById.TryGetValue(e.TweakId, out var item) ? item.Name : e.TweakId,
                e.Timestamp,
                e.Success,
                e.Message))
            .ToList();

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
    public RelayCommand VerifyCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand ApplyPresetCommand { get; }

    public HomeTabViewModel(ILocalizationService localization, TweakLibrary library, TweakEngine engine, BatchRunner batchRunner,
        ConflictDetector conflictDetector, SystemContext systemContext, IJournalStore journal, PcSpecs pcSpecs)
    {
        Localization = localization;
        _library = library;
        _engine = engine;
        _batchRunner = batchRunner;
        _conflictDetector = conflictDetector;
        _systemContext = systemContext;
        _journal = journal;
        PcSpecs = pcSpecs;
        Presets = library.Presets.Select(p => new PresetViewModel(p, localization)).ToList();

        ApplyCommand = new RelayCommand(async () => await ApplyAsync(), () => !IsApplying && PendingCount > 0);
        VerifyCommand = new RelayCommand(async () => await VerifyAsync(), () => !IsApplying);
        RevertCommand = new RelayCommand(async id => await RevertAsync((string)id!), _ => !IsApplying);
        ApplyPresetCommand = new RelayCommand(id => _library.ApplyPreset((string)id!));
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

        await RunExclusiveAsync(async () =>
        {
            var result = await _library.ApplyPendingAsync(_batchRunner, _systemContext);
            var failed = result.Outcomes.Count(o => !o.Success);
            ResultHasFailures = failed > 0;
            ResultMessage = failed == 0
                ? $"Применено успешно: {result.Outcomes.Count}"
                : $"Применено: {result.Outcomes.Count - failed} из {result.Outcomes.Count}, ошибок: {failed}";
        });

        OnPendingChanged(this, EventArgs.Empty);
        OnPropertyChanged(nameof(ActiveTweaks));
        OnPropertyChanged(nameof(RecentJournalEntries));
    }

    private async Task VerifyAsync()
    {
        await RunExclusiveAsync(async () =>
        {
            var results = await _library.VerifyActiveTweaksAsync(_engine);
            var drifted = results.Where(r => !r.Verified).ToList();
            ResultHasFailures = drifted.Count > 0;
            ResultMessage = results.Count == 0
                ? "Нет активных твиков для проверки"
                : drifted.Count == 0
                    ? $"Проверено: {results.Count}, всё соответствует ожидаемому"
                    : $"Проверено: {results.Count}, слетело: {drifted.Count} ({string.Join(", ", drifted.Select(d => d.Item.Name))})";
        });
    }

    private async Task RevertAsync(string tweakId)
    {
        await RunExclusiveAsync(async () =>
        {
            var outcome = await _library.RevertTweakAsync(_engine, tweakId);
            ResultHasFailures = !outcome.Success;
            ResultMessage = outcome.Success ? "Откачено к состоянию до применения" : $"Не удалось откатить: {outcome.Message}";
        });

        OnPropertyChanged(nameof(ActiveTweaks));
        OnPropertyChanged(nameof(RecentJournalEntries));
    }

    private async Task RunExclusiveAsync(Func<Task> action)
    {
        IsApplying = true;
        ApplyCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        ResultMessage = null;
        ResultHasFailures = false;

        try
        {
            await action();
        }
        finally
        {
            IsApplying = false;
            ApplyCommand.NotifyCanExecuteChanged();
            VerifyCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>Одна запись панели журнала на «Главной» — с уже разрешённым именем твика вместо сырого id.</summary>
public sealed record JournalEntryDisplay(string TweakName, DateTimeOffset Timestamp, bool Success, string? Message);
