using Observation.App.Services;
using Observation.Core.Scripts;
using Observation.Handlers.Scripts;

namespace Observation.App.ViewModels;

/// <summary>
/// «Очистка системы»: обычные твики-тумблеры из TweakLibrary (сейчас — только Reserved
/// Storage) плюс разовые действия очистки (temp/WU-кэш/кэш Chrome — ScriptItemViewModel,
/// тот же паттерн, что и вкладка «Скрипты», см. план: очистка — это действия, не настройки).
/// </summary>
public sealed class CleanTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<TweakGroupViewModel> Groups { get; }
    public IReadOnlyList<ScriptItemViewModel> Actions { get; }

    public CleanTabViewModel(ILocalizationService localization, IReadOnlyList<TweakGroupViewModel> groups,
        IReadOnlyList<ScriptDefinition> scripts, PowerShellScriptRunner runner)
    {
        Localization = localization;
        Groups = groups;
        Actions = scripts.Where(s => s.Tab == "clean")
            .Select(s => new ScriptItemViewModel(s, runner, localization))
            .ToList();
    }
}
