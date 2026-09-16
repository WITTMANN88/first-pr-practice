namespace Observation.Core.Conflicts;

/// <summary>
/// Стартовый набор пар из «Обнаружение конфликтов в пакете» в плане. Межтвиковая логика,
/// не свойство одного твика — поэтому статическая таблица здесь, а не в JSON-реестре твиков.
/// Id твиков — заглушки по конвенции "вкладка.имя" из примера схемы твика; должны быть
/// синхронизированы с реальными id, когда соответствующие твики попадут в JSON-реестр.
/// Список дополняется по мере находок, см. план.
/// </summary>
public static class ConflictRuleCatalog
{
    public static readonly IReadOnlyList<ConflictRule> Rules = new[]
    {
        new ConflictRule(
            "security.defender.disable", "security.cfa.enable", ConflictSeverity.Contradictory,
            "CFA — часть Defender, работать без него не может",
            "Controlled Folder Access is part of Defender and cannot function without it"),

        new ConflictRule(
            "security.vbs.disable", "security.bitlocker.enable", ConflictSeverity.Contradictory,
            "Отключение VBS меняет измерения безопасной загрузки — BitLocker может потребовать ключ восстановления при следующей загрузке",
            "Disabling VBS changes secure boot measurements — BitLocker may demand a recovery key on next boot"),

        new ConflictRule(
            "apps.edge.remove", "apps.edge.debloat", ConflictSeverity.Contradictory,
            "Нечего деблоатить у удалённого браузера",
            "Nothing to debloat in a browser that has been removed"),

        new ConflictRule(
            "updates.disable", "updates.defer", ConflictSeverity.Redundant,
            "Отложенные обновления не имеют смысла при выключенной службе целиком",
            "Deferred updates are meaningless while the update service itself is disabled"),

        new ConflictRule(
            "scripts.reset_windows_update", "updates.disable", ConflictSeverity.Contradictory,
            "Сброс запускает службу, отключение её тут же гасит — порядок операций даст непредсказуемый результат",
            "The reset starts the service, the disable stops it right after — operation order gives an unpredictable result")
    };
}
