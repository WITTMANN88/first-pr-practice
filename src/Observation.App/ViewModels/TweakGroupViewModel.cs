using System.ComponentModel;
using Observation.App.Services;
using Observation.Core.Tweaks;

namespace Observation.App.ViewModels;

/// <summary>
/// Твики одной вкладки, сгруппированные по полю "group" JSON-реестра. GroupName —
/// вычисляемое свойство (как Name/Description у TweakItemViewModel), а не голая строка:
/// на реальной машине выяснилось, что Group изначально был обычным string, а не
/// LocalizedText — заголовки групп не переключались на EN вместе с остальным интерфейсом.
/// </summary>
public sealed class TweakGroupViewModel : ViewModelBase
{
    private readonly LocalizedText _groupName;
    private readonly ILocalizationService _localization;

    public string GroupName => _groupName.Get(_localization.CurrentLanguage);
    public IReadOnlyList<TweakItemViewModel> Items { get; }

    public TweakGroupViewModel(LocalizedText groupName, ILocalizationService localization, IReadOnlyList<TweakItemViewModel> items)
    {
        _groupName = groupName;
        _localization = localization;
        Items = items;
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ILocalizationService.CurrentLanguage) or "Item[]")
            OnPropertyChanged(nameof(GroupName));
    }
}
