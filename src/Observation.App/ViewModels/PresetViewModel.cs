using System.ComponentModel;
using Observation.App.Services;
using Observation.Core.Presets;

namespace Observation.App.ViewModels;

/// <summary>Обёртка PresetDefinition для привязки в интерфейсе — тот же приём, что и TweakGroupViewModel для Group.</summary>
public sealed class PresetViewModel : ViewModelBase
{
    private readonly PresetDefinition _definition;
    private readonly ILocalizationService _localization;

    public string Id => _definition.Id;
    public string Name => _definition.Name.Get(_localization.CurrentLanguage);
    public string Description => _definition.Description.Get(_localization.CurrentLanguage);

    public PresetViewModel(PresetDefinition definition, ILocalizationService localization)
    {
        _definition = definition;
        _localization = localization;
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ILocalizationService.CurrentLanguage) or "Item[]")
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
        }
    }
}
