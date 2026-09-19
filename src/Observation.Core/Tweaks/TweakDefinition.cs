namespace Observation.Core.Tweaks;

/// <summary>Один твик из JSON-реестра твиков — см. «Схема твика (JSON)» в плане.</summary>
public sealed class TweakDefinition
{
    public required string Id { get; init; }
    public required string Tab { get; init; }
    public required LocalizedText Group { get; init; }
    public required LocalizedText Name { get; init; }
    public required LocalizedText Description { get; init; }
    public required Severity Severity { get; init; }
    public required ControlType ControlType { get; init; }
    public bool RequiresReboot { get; init; }
    public ApplicabilityRule Applicability { get; init; } = ApplicabilityRule.Always;
    public required ApplySpec Apply { get; init; }
    public required VerifySpec Verify { get; init; }
    public required RevertSpec Revert { get; init; }
    public LocalizedText? Info { get; init; }
}
