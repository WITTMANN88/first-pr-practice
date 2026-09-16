using System.ComponentModel;
using Observation.App.Services;
using Observation.Core.Tweaks;

namespace Observation.App.ViewModels;

/// <summary>
/// Обёртка одного TweakDefinition для привязки в интерфейсе. IsOn пока лишь отражает
/// состояние тумблера в UI — фактическое применение через TweakEngine/BatchRunner
/// подключается на следующем шаге (сводка + применение), не в этом архитектурном проходе.
/// </summary>
public sealed class TweakItemViewModel : ViewModelBase
{
    public TweakDefinition Definition { get; }
    private readonly ILocalizationService _localization;

    public string Name => Definition.Name.Get(_localization.CurrentLanguage);
    public string Description => Definition.Description.Get(_localization.CurrentLanguage);

    public string SeverityLabel => _localization[Definition.Severity switch
    {
        Severity.Safe => "SeveritySafe",
        Severity.Situational => "SeveritySituational",
        _ => "SeverityRisky"
    }];

    private bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set => SetField(ref _isOn, value);
    }

    public TweakItemViewModel(TweakDefinition definition, ILocalizationService localization)
    {
        Definition = definition;
        _localization = localization;
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ILocalizationService.CurrentLanguage) or "Item[]")
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(SeverityLabel));
        }
    }
}
