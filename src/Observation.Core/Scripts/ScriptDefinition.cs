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

    /// <summary>Встроенные скрипты из JSON-реестра. Ровно одно из ScriptText/FilePath должно
    /// быть задано — см. FilePath для пользовательских .ps1/.bat, добавленных через диалог.</summary>
    public string? ScriptText { get; init; }

    /// <summary>Путь к пользовательскому .ps1/.bat/.cmd, добавленному через файловый диалог
    /// на вкладке «Скрипты» — не хранится в JSON-реестре, живёт только в течение сессии
    /// (см. ScriptsTabViewModel.AddCustomScriptCommand, сознательно без сохранения на диск).</summary>
    public string? FilePath { get; init; }

    public bool RequiresReboot { get; init; }
}
