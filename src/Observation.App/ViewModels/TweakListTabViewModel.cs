using Observation.App.Services;
using Observation.Core.Tweaks;

namespace Observation.App.ViewModels;

/// <summary>
/// Генератор списка твиков вкладки из JSON-реестра — "новая функция = запись в реестре,
/// а не новый код UI" (см. «Журнал решений»). Один экземпляр на вкладку, отфильтрованную
/// по TweakDefinition.Tab, сгруппированную по TweakDefinition.Group.
/// </summary>
public sealed class TweakListTabViewModel : ViewModelBase
{
    public IReadOnlyList<TweakGroupViewModel> Groups { get; }
    public bool IsEmpty => Groups.Count == 0;
    public ILocalizationService Localization { get; }

    public TweakListTabViewModel(ILocalizationService localization, IReadOnlyList<TweakDefinition> tabTweaks)
    {
        Localization = localization;
        Groups = tabTweaks
            // Ru как стабильный ключ группировки — сам LocalizedText не Equatable,
            // а Ru гарантированно заполнен схемой (required).
            .GroupBy(t => t.Group.Ru)
            .Select(g => new TweakGroupViewModel(
                g.First().Group,
                localization,
                g.Select(t => new TweakItemViewModel(t, localization)).ToList()))
            .ToList();
    }
}
