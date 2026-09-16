using Observation.App.Services;
using Observation.App.ViewModels;
using Observation.Core.Batch;
using Observation.Core.Engine;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;

namespace Observation.App.Runtime;

/// <summary>
/// Единственный источник правды по состоянию всех твиков — один TweakItemViewModel
/// на твик, переиспользуемый всеми вкладками (см. TweakItemViewModel). Отсюда же —
/// накопительная очередь (PendingChanges) и её применение через BatchRunner.
/// </summary>
public sealed class TweakLibrary
{
    private readonly ILocalizationService _localization;

    public IReadOnlyList<TweakDefinition> AllTweaks { get; }
    public IReadOnlyDictionary<string, TweakItemViewModel> ItemsById { get; }

    /// <summary>Твик в очереди на применение — его IsOn отличается от последнего известного фактического состояния.</summary>
    public IReadOnlyList<TweakItemViewModel> PendingChanges =>
        ItemsById.Values.Where(i => i.IsOn != i.BaselineOn).ToList();

    /// <summary>Поднимается при изменении IsOn любого твика — вкладка «Главная» обновляет счётчик очереди.</summary>
    public event EventHandler? PendingChanged;

    public TweakLibrary(ILocalizationService localization, IReadOnlyList<TweakDefinition> allTweaks)
    {
        _localization = localization;
        AllTweaks = allTweaks;

        var items = new Dictionary<string, TweakItemViewModel>();
        foreach (var tweak in allTweaks)
        {
            var item = new TweakItemViewModel(tweak, localization);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TweakItemViewModel.IsOn))
                    PendingChanged?.Invoke(this, EventArgs.Empty);
            };
            items[tweak.Id] = item;
        }

        ItemsById = items;
    }

    public IReadOnlyList<TweakGroupViewModel> GroupsForTab(string tabId) =>
        AllTweaks.Where(t => t.Tab == tabId)
            .GroupBy(t => t.Group.Ru)
            .Select(g => new TweakGroupViewModel(
                g.First().Group,
                _localization,
                g.Select(t => ItemsById[t.Id]).ToList()))
            .ToList();

    /// <summary>
    /// Читает фактическое состояние каждого твика через TweakEngine.VerifyAsync(desiredOn: true) —
    /// совпадение с "включённым" значением означает "сейчас включён", иначе считаем выключенным.
    /// Только чтение (registry-read/service-query/handler.Verify), без побочных эффектов.
    /// Отказ по отдельному твику (недоступен реестр и т.п.) — не критично, считаем его выключенным.
    /// </summary>
    public async Task ProbeInitialStatesAsync(TweakEngine engine)
    {
        foreach (var item in ItemsById.Values)
        {
            bool isOn;
            try
            {
                isOn = await engine.VerifyAsync(item.Definition, desiredOn: true);
            }
            catch (Exception)
            {
                isOn = false;
            }

            item.InitializeState(isOn);
        }
    }

    public async Task<BatchRunResult> ApplyPendingAsync(BatchRunner runner, SystemContext systemContext)
    {
        var pending = PendingChanges;
        var queue = pending.Select(i => new QueuedTweak(i.Definition, i.IsOn)).ToList();
        var result = await runner.RunAsync(queue, systemContext);

        foreach (var outcome in result.Outcomes)
        {
            if (!ItemsById.TryGetValue(outcome.TweakId, out var item))
                continue;

            if (outcome.Success)
                item.CommitBaseline();
            else
                item.RevertToBaseline();
        }

        return result;
    }
}
