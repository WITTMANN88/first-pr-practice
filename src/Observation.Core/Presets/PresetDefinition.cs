using Observation.Core.Tweaks;

namespace Observation.Core.Presets;

/// <summary>
/// Пресет с «Главной» — просто список id твиков, которые он выставляет во включённое
/// состояние (см. план: «Пресет только выставляет тумблеры по вкладкам — ничего не
/// применяется, пока не нажата общая кнопка «Применить»»). Не трогает твики вне списка.
/// </summary>
public sealed class PresetDefinition
{
    public required string Id { get; init; }
    public required LocalizedText Name { get; init; }
    public required LocalizedText Description { get; init; }
    public required IReadOnlyList<string> TweakIds { get; init; }
}
