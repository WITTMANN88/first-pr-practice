namespace Observation.Core.Conflicts;

/// <summary>Сканирует очередь перед экраном сводки — порядок выбора твиков не важен.</summary>
public sealed class ConflictDetector
{
    private readonly IReadOnlyList<ConflictRule> _rules;

    public ConflictDetector(IReadOnlyList<ConflictRule>? rules = null) => _rules = rules ?? ConflictRuleCatalog.Rules;

    public IReadOnlyList<ConflictRule> Scan(IEnumerable<string> queuedTweakIds)
    {
        var set = new HashSet<string>(queuedTweakIds, StringComparer.OrdinalIgnoreCase);
        return _rules.Where(r => set.Contains(r.TweakIdA) && set.Contains(r.TweakIdB)).ToList();
    }
}
