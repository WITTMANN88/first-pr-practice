using Observation.Core.Tweaks;

namespace Observation.Core.Scripts;

/// <summary>
/// Один пункт вкладки «Скрипты» (или встроенный раздел на «Очистке») — разовое действие,
/// выполняется сразу по нажатию со своим подтверждением, не через накопительную очередь
/// «Применить» (см. план, «Вкладка 9 — Скрипты»: «здесь не тумблеры с накопительным
/// применением, а разовые действия»). Нет apply/verify/revert — нечего проверять и
/// откатывать у одноразовой команды.
/// </summary>
public sealed class ScriptDefinition
{
    public required string Id { get; init; }
    public required string Tab { get; init; }
    public required LocalizedText Group { get; init; }
    public required LocalizedText Name { get; init; }
    public required LocalizedText Description { get; init; }
    public required Severity Severity { get; init; }
    public required string ScriptText { get; init; }
    public bool RequiresReboot { get; init; }
}
