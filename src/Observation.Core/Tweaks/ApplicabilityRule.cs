using Observation.Core.SystemAccess;

namespace Observation.Core.Tweaks;

/// <summary>
/// Условная применимость твика — версия/сборка Windows, редакция, модель процессора.
/// "Type" — описательная метка из плана ("always", "cpu-not-in-list"); реальная оценка
/// ведётся по присутствующим полям, а не по строгому соответствию Type, потому что план
/// иллюстрирует эту схему двумя не полностью согласованными формами JSON — это единый
/// синтез обеих, а не буквальная цитата одной из них.
/// </summary>
public sealed class ApplicabilityRule
{
    public string Type { get; init; } = "always";
    public int? MinBuild { get; init; }
    public IReadOnlyList<string>? Editions { get; init; }
    public IReadOnlyList<string>? CpuNotIn { get; init; }

    /// <summary>Форма "{ type: cpu-not-in-list, list: [...] }" из примера в плане — синоним CpuNotIn.</summary>
    public IReadOnlyList<string>? List { get; init; }

    public static ApplicabilityRule Always { get; } = new() { Type = "always" };

    public ApplicabilityResult Evaluate(SystemContext ctx)
    {
        if (MinBuild is int minBuild && ctx.BuildNumber < minBuild)
            return ApplicabilityResult.NotApplicable($"требует сборку Windows {minBuild} или новее (текущая: {ctx.BuildNumber})");

        if (Editions is { Count: > 0 } editions && !editions.Contains(ctx.EditionId, StringComparer.OrdinalIgnoreCase))
            return ApplicabilityResult.NotApplicable($"требует редакцию: {string.Join(", ", editions)}");

        var cpuExclusionList = CpuNotIn ?? List;
        if (cpuExclusionList is { Count: > 0 } && ctx.CpuModel is { Length: > 0 } cpu &&
            cpuExclusionList.Any(excluded => cpu.Contains(excluded, StringComparison.OrdinalIgnoreCase)))
        {
            return ApplicabilityResult.NotApplicable("не подходит для данной модели процессора");
        }

        return ApplicabilityResult.Applicable;
    }
}

public readonly record struct ApplicabilityResult(bool IsApplicable, string? Reason)
{
    public static ApplicabilityResult Applicable { get; } = new(true, null);
    public static ApplicabilityResult NotApplicable(string reason) => new(false, reason);
}
