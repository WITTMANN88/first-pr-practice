using Observation.App.Services;
using Observation.App.ViewModels;
using Observation.Core.Batch;
using Observation.Core.Engine;
using Observation.Core.Journal;
using Observation.Core.Presets;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;

namespace Observation.App.Runtime;

/// <summary>
/// Единственный источник правды по состоянию всех твиков — один TweakItemViewModel
/// на твик, переиспользуемый всеми вкладками (см. TweakItemViewModel). Отсюда же —
/// накопительная очередь (PendingChanges) и её применение через BatchRunner, а также
/// «активные твики» из журнала (перепроверка/точечный откат — мех. надёжности №1 из плана).
/// </summary>
public sealed class TweakLibrary
{
    private readonly ILocalizationService _localization;
    private readonly IJournalStore _journal;

    public IReadOnlyList<TweakDefinition> AllTweaks { get; }
    public IReadOnlyDictionary<string, TweakItemViewModel> ItemsById { get; }
    public IReadOnlyList<PresetDefinition> Presets { get; }

    /// <summary>Твик в очереди на применение — его IsOn отличается от последнего известного фактического состояния.</summary>
    public IReadOnlyList<TweakItemViewModel> PendingChanges =>
        ItemsById.Values.Where(i => i.IsOn != i.BaselineOn).ToList();

    /// <summary>Поднимается при изменении IsOn любого твика — вкладка «Главная» обновляет счётчик очереди.</summary>
    public event EventHandler? PendingChanged;

    public TweakLibrary(ILocalizationService localization, IReadOnlyList<TweakDefinition> allTweaks, IJournalStore journal,
        SystemContext systemContext, IReadOnlyList<PresetDefinition>? presets = null)
    {
        _localization = localization;
        _journal = journal;
        AllTweaks = allTweaks;
        Presets = presets ?? Array.Empty<PresetDefinition>();

        var items = new Dictionary<string, TweakItemViewModel>();
        foreach (var tweak in allTweaks)
        {
            var item = new TweakItemViewModel(tweak, localization, systemContext);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TweakItemViewModel.IsOn))
                    PendingChanged?.Invoke(this, EventArgs.Empty);
            };
            items[tweak.Id] = item;
        }

        ItemsById = items;
    }

    /// <summary>Выставляет IsOn=true для твиков пресета (только тех, что реально есть в реестре) — ничего не применяет и не трогает твики вне списка (см. план: «Пресет только выставляет тумблеры»).</summary>
    public void ApplyPreset(string presetId)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset is null)
            return;

        foreach (var tweakId in preset.TweakIds)
        {
            if (ItemsById.TryGetValue(tweakId, out var item))
                item.IsOn = true;
        }
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

        // BatchRunner.RunAsync уже сам обновляет "активные твики" в журнале (restore-previous/
        // additive-tagged) — здесь дублировать не нужно, GetRevertibleTweaks просто читает их обратно.
        return result;
    }

    /// <summary>«Активные твики» из журнала (мех. надёжности №1) — только с восстанавливаемым
    /// откатом (restore-previous/additive-tagged), только для текущего пользователя (без SID).</summary>
    public IReadOnlyList<TweakItemViewModel> GetRevertibleTweaks() =>
        _journal.GetActiveTweaks()
            .Where(a => a.UserSid is null)
            .Select(a => ItemsById.TryGetValue(a.TweakId, out var item) ? item : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

    /// <summary>Перечитывает фактическое состояние каждого активного твика и сверяет с ожидаемым — «слетевшие» возвращаются с Verified=false.</summary>
    public async Task<IReadOnlyList<(TweakItemViewModel Item, bool Verified)>> VerifyActiveTweaksAsync(TweakEngine engine)
    {
        var results = new List<(TweakItemViewModel, bool)>();
        foreach (var item in GetRevertibleTweaks())
        {
            bool verified;
            try
            {
                verified = await engine.VerifyAsync(item.Definition, item.BaselineOn);
            }
            catch (Exception)
            {
                verified = false;
            }

            results.Add((item, verified));
        }

        return results;
    }

    /// <summary>Точечный откат одного активного твика к состоянию "до" последнего успешного применения (не к offData).</summary>
    public async Task<TweakOutcome> RevertTweakAsync(TweakEngine engine, string tweakId)
    {
        if (!ItemsById.TryGetValue(tweakId, out var item))
            return TweakOutcome.Failed(tweakId, "Твик не найден в реестре");

        var entry = _journal.GetLatestEntryForTweak(tweakId);
        if (entry is null)
            return TweakOutcome.Failed(tweakId, "Нет сохранённой записи для отката");

        var outcome = await engine.RevertAsync(item.Definition, entry.PreviousValueJson);
        if (outcome.Success)
        {
            _journal.RemoveActiveTweak(tweakId, userSid: null);
            var isOnNow = await engine.VerifyAsync(item.Definition, desiredOn: true);
            item.InitializeState(isOnNow);
        }

        return outcome;
    }
}
