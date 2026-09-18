using Observation.App.Services;
using Observation.Core.Scripts;
using Observation.Handlers.Scripts;

namespace Observation.App.ViewModels;

/// <summary>«Скрипты» — только разовые действия (см. ScriptItemViewModel), без накопительной очереди/тумблеров.</summary>
public sealed class ScriptsTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<ScriptItemViewModel> Scripts { get; }

    public ScriptsTabViewModel(ILocalizationService localization, IReadOnlyList<ScriptDefinition> scripts, PowerShellScriptRunner runner)
    {
        Localization = localization;
        Scripts = scripts.Where(s => s.Tab == "scripts").Select(s => new ScriptItemViewModel(s, runner, localization)).ToList();
    }
}
