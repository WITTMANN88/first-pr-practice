using Observation.App.Services;
using Observation.Core.Tweaks;

namespace Observation.App.ViewModels;

/// <summary>Одна группа сводки — все ожидающие твики одной вкладки (group by Definition.Tab).</summary>
public sealed class ApplySummaryGroup
{
    public string TabName { get; }
    public IReadOnlyList<TweakItemViewModel> Items { get; }

    public ApplySummaryGroup(string tabName, IReadOnlyList<TweakItemViewModel> items)
    {
        TabName = tabName;
        Items = items;
    }
}

/// <summary>
/// «Экран сводки перед применением» из плана (вкладка 1 — Главная / «Механизм безопасности
/// и отката») — список того, что применится, сгруппированный по вкладкам, плюс обязательное
/// подтверждение, если в очереди есть риск-твики или обнаружен конфликт между ними.
/// </summary>
public sealed class ApplySummaryViewModel : ViewModelBase
{
    private static readonly Dictionary<string, string> TabLocalizationKeys = new()
    {
        ["home"] = "NavHome",
        ["perf"] = "NavPerf",
        ["privacy"] = "NavPrivacy",
        ["security"] = "NavSecurity",
        ["debloat"] = "NavDebloat",
        ["clean"] = "NavClean",
        ["ui"] = "NavUi",
        ["apps"] = "NavApps",
        ["scripts"] = "NavScripts",
        ["net"] = "NavNet",
        ["autostart"] = "NavAutostart",
        ["updates"] = "NavUpdates",
        ["diag"] = "NavDiag"
    };

    public ILocalizationService Localization { get; }
    public IReadOnlyList<ApplySummaryGroup> Groups { get; }
    public IReadOnlyList<string> ConflictMessages { get; }
    public bool HasConflicts => ConflictMessages.Count > 0;
    public bool HasRiskyItems { get; }
    public bool RequiresAcknowledgement => HasRiskyItems || HasConflicts;

    private bool _acknowledgeRisky;
    public bool AcknowledgeRisky
    {
        get => _acknowledgeRisky;
        set
        {
            if (SetField(ref _acknowledgeRisky, value))
                ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>true — подтверждено (кнопка «Применить»), false — отмена.</summary>
    public event Action<bool>? Closed;

    public ApplySummaryViewModel(ILocalizationService localization, IReadOnlyList<TweakItemViewModel> pending, IReadOnlyList<string> conflictMessages)
    {
        Localization = localization;
        ConflictMessages = conflictMessages;
        HasRiskyItems = pending.Any(p => p.Definition.Severity == Severity.Risky);
        Groups = pending
            .GroupBy(p => p.Definition.Tab)
            .Select(g => new ApplySummaryGroup(TabDisplayName(g.Key), g.ToList()))
            .ToList();

        ConfirmCommand = new RelayCommand(() => Closed?.Invoke(true), () => !RequiresAcknowledgement || AcknowledgeRisky);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(false));
    }

    private string TabDisplayName(string tabId) =>
        TabLocalizationKeys.TryGetValue(tabId, out var key) ? Localization[key] : tabId;
}
