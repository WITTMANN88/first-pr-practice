using System.ComponentModel;
using Observation.App.Services;
using Observation.Core.SystemAccess;
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

    public string IsOnLabel => _localization[IsOn ? "StateOn" : "StateOff"];

    public string InfoText => Definition.Info?.Get(_localization.CurrentLanguage) ?? string.Empty;

    /// <summary>
    /// Из макета: строка твика, несовместимого с этой машиной (сборка Windows/редакция/CPU —
    /// см. ApplicabilityRule), должна быть заранее серой с пояснением "почему", а не просто
    /// молча падать при "Применить". Оценивается один раз при создании — SystemContext не
    /// меняется за время жизни процесса.
    /// </summary>
    public bool IsApplicableHere { get; }
    public string? ApplicabilityReason { get; }

    private bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetField(ref _isOn, value))
                OnPropertyChanged(nameof(IsOnLabel));
        }
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

    public TweakItemViewModel(TweakDefinition definition, ILocalizationService localization, SystemContext systemContext)
    {
        Definition = definition;
        _localization = localization;
        _localization.PropertyChanged += OnLocalizationChanged;

        var applicability = definition.Applicability.Evaluate(systemContext);
        IsApplicableHere = applicability.IsApplicable;
        ApplicabilityReason = applicability.Reason;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ILocalizationService.CurrentLanguage) or "Item[]")
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(SeverityLabel));
            OnPropertyChanged(nameof(IsOnLabel));
            OnPropertyChanged(nameof(InfoText));
        }
    }
}
