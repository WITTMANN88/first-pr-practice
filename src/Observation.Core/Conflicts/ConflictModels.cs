namespace Observation.Core.Conflicts;

/// <summary>contradictory — жёсткое противоречие; redundant — бессмысленное, но не опасное сочетание.</summary>
public enum ConflictSeverity
{
    Contradictory,
    Redundant
}

public sealed record ConflictRule(string TweakIdA, string TweakIdB, ConflictSeverity Severity, string MessageRu, string MessageEn);
