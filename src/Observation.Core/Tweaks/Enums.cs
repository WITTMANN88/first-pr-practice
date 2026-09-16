using System.Text.Json.Serialization;

namespace Observation.Core.Tweaks;

/// <summary>Риск-категория твика — совпадает с sevtag в дизайне интерфейса (безопасно/по ситуации/риск).</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<Severity>))]
public enum Severity
{
    Safe,
    Situational,
    Risky
}

/// <summary>Тип элемента управления твиком в интерфейсе.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<ControlType>))]
public enum ControlType
{
    Toggle,
    Segmented,
    Action
}

/// <summary>
/// Пять типов отката твика — см. «Механизм отката — по типам» в плане.
/// RestorePrevious/AdditiveTagged поддерживают автоматический откат в TweakEngine;
/// BestEffort/Unavailable/ManualGuided — честно не поддерживают (см. правило про отказ
/// от применения при неудачном захвате состояния «до»).
/// </summary>
[JsonConverter(typeof(KebabCaseEnumConverter<RevertType>))]
public enum RevertType
{
    RestorePrevious,
    AdditiveTagged,
    BestEffort,
    Unavailable,
    ManualGuided
}
