using System.ComponentModel;
using Observation.App.Services;
using Observation.Core.Tweaks;

namespace Observation.App.ViewModels;

/// <summary>
/// Обёртка одного TweakDefinition для привязки в интерфейсе. Один экземпляр на твик,
/// живёт в TweakLibrary и переиспользуется между вкладками — состояние тумблера не
/// теряется при навигации. BaselineOn — последнее известное фактическое состояние
/// (после probe при старте или после успешного применения); IsOn != BaselineOn значит
/// твик в очереди на применение (см. TweakLibrary.PendingChanges).
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

    public bool BaselineOn { get; private set; }

    /// <summary>Задаёт и IsOn, и BaselineOn — используется при первичном probe состояния системы.</summary>
    public void InitializeState(bool isOn)
    {
        BaselineOn = isOn;
        IsOn = isOn;
    }

    /// <summary>Твик успешно применён — текущее IsOn становится новым baseline.</summary>
    public void CommitBaseline() => BaselineOn = IsOn;

    /// <summary>Применение не удалось — тумблер в интерфейсе возвращается к фактическому состоянию.</summary>
    public void RevertToBaseline() => IsOn = BaselineOn;

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
