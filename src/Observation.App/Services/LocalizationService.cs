using System.ComponentModel;

namespace Observation.App.Services;

public sealed class LocalizationService : ILocalizationService
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _currentLanguage = "ru";
    public string CurrentLanguage => _currentLanguage;

    // Строки каркаса приложения (сайдбар, About) — взяты из уже опубликованного макета
    // (ui-preview.html), чтобы не расходиться. Тексты содержимого вкладок появятся вместе
    // с реальной стилизацией каждой вкладки — это отдельный, более поздний проход.
    private static readonly Dictionary<string, string> Ru = new()
    {
        ["AppTitle"] = "OBSERVATION",
        ["NavHome"] = "Главная",
        ["NavPerf"] = "Производительность и игры",
        ["NavPrivacy"] = "Приватность и телеметрия",
        ["NavSecurity"] = "Безопасность",
        ["NavDebloat"] = "Деблоат и UWP",
        ["NavClean"] = "Очистка системы",
        ["NavUi"] = "Кастомизация интерфейса",
        ["NavApps"] = "Установка программ",
        ["NavScripts"] = "Скрипты",
        ["NavNet"] = "Сеть",
        ["NavAutostart"] = "Автозагрузка и службы",
        ["NavUpdates"] = "Обновления Windows",
        ["NavDiag"] = "Диагностика и драйверы",
        ["AboutTag"] = "Своя программа для тонкой настройки Windows",
        ["AboutVersion"] = "v0.1.0 · черновик",
        ["AboutLicense"] = "MIT License",
        ["AboutFuture"] = "Ссылки на репозиторий и канал появятся здесь позже",
        ["AboutClose"] = "Закрыть",
        ["TrayShow"] = "Показать",
        ["TrayExit"] = "Выход",
        ["HomePlaceholder"] = "Пресеты и сводка системы — отдельный проход по стилизации. Здесь — реальная очередь применения по всем вкладкам.",
        ["HomePendingLabel"] = "Ожидают применения:",
        ["HomeApplyBtn"] = "Применить",
        ["HomeApplyingBtn"] = "Применяю…",
        ["HomeActiveTweaksLabel"] = "Активные твики:",
        ["HomeVerifyBtn"] = "Проверить состояние",
        ["HomeRevertBtn"] = "Отменить",
        ["HomePresetsLabel"] = "Пресеты:",
        ["HomeSpecsLabel"] = "Характеристики ПК",
        ["HomeSpecsOs"] = "ОС",
        ["HomeSpecsCpu"] = "CPU",
        ["HomeSpecsGpu"] = "GPU",
        ["HomeSpecsRam"] = "RAM",
        ["HomeSpecsDisk"] = "Диск",
        ["HomeJournalLabel"] = "Журнал изменений",
        ["HomeJournalEmpty"] = "Журнал пуст — ещё ничего не применялось.",
        ["TweakListEmpty"] = "Для этой вкладки пока нет твиков в загруженном реестре.",
        ["SeveritySafe"] = "безопасно",
        ["SeveritySituational"] = "по ситуации",
        ["SeverityRisky"] = "риск",
        ["StateOn"] = "вкл",
        ["StateOff"] = "выкл",
        ["DebloatUwpHeader"] = "Установленные UWP-приложения",
        ["DebloatScanBtn"] = "Сканировать",
        ["DebloatScanningBtn"] = "Сканирую…",
        ["DebloatRemoveBtn"] = "Удалить",
        ["AppsWingetHeader"] = "Каталог winget",
        ["AppsWingetMissing"] = "winget не найден",
        ["AppsInstallBtn"] = "Установить",
        ["AppsInstallingBtn"] = "Устанавливаю…",
        ["AppsInstalledBtn"] = "Готово",
        ["AppsRetryBtn"] = "Повторить",
        ["AppsRuntimeBundleBtn"] = "Установить VC++ redist + .NET Desktop Runtime",
        ["AppsLogHeader"] = "Журнал установки",
        ["ScriptRunBtn"] = "Выполнить",
        ["ScriptRunningBtn"] = "Выполняю…",
        ["CleanActionsHeader"] = "Разовые действия",
        ["DiagDevicesHeader"] = "Проблемные устройства",
        ["DiagDevicesEmpty"] = "Проблемных устройств не найдено.",
        ["DiagSearchBtn"] = "Искать в интернете",
        ["DiagQuickLinksHeader"] = "Быстрые ссылки",
        ["DiagDeviceManagerBtn"] = "Диспетчер устройств",
        ["DiagSystemRestoreBtn"] = "Восстановление системы",
        ["DiagControlPanelBtn"] = "Панель управления",
        ["DiagSystemPropertiesBtn"] = "Свойства системы",
        ["DiagProgramsFeaturesBtn"] = "Программы и компоненты"
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["AppTitle"] = "OBSERVATION",
        ["NavHome"] = "Home",
        ["NavPerf"] = "Performance & Gaming",
        ["NavPrivacy"] = "Privacy & Telemetry",
        ["NavSecurity"] = "Security",
        ["NavDebloat"] = "Debloat & UWP",
        ["NavClean"] = "System Cleaning",
        ["NavUi"] = "UI Customization",
        ["NavApps"] = "App Installer",
        ["NavScripts"] = "Scripts",
        ["NavNet"] = "Network",
        ["NavAutostart"] = "Startup & Services",
        ["NavUpdates"] = "Windows Update",
        ["NavDiag"] = "Diagnostics & Drivers",
        ["AboutTag"] = "A dedicated app for fine-tuning Windows",
        ["AboutVersion"] = "v0.1.0 · draft",
        ["AboutLicense"] = "MIT License",
        ["AboutFuture"] = "Links to the repository and channel will appear here later",
        ["AboutClose"] = "Close",
        ["TrayShow"] = "Show",
        ["TrayExit"] = "Exit",
        ["HomePlaceholder"] = "Presets and system summary are a separate styling pass. This is the real apply queue across all tabs.",
        ["HomePendingLabel"] = "Pending changes:",
        ["HomeApplyBtn"] = "Apply",
        ["HomeApplyingBtn"] = "Applying…",
        ["HomeActiveTweaksLabel"] = "Active tweaks:",
        ["HomeVerifyBtn"] = "Check status",
        ["HomeRevertBtn"] = "Undo",
        ["HomePresetsLabel"] = "Presets:",
        ["HomeSpecsLabel"] = "PC specs",
        ["HomeSpecsOs"] = "OS",
        ["HomeSpecsCpu"] = "CPU",
        ["HomeSpecsGpu"] = "GPU",
        ["HomeSpecsRam"] = "RAM",
        ["HomeSpecsDisk"] = "Disk",
        ["HomeJournalLabel"] = "Change journal",
        ["HomeJournalEmpty"] = "The journal is empty — nothing has been applied yet.",
        ["TweakListEmpty"] = "No tweaks for this tab in the loaded registry yet.",
        ["SeveritySafe"] = "safe",
        ["SeveritySituational"] = "situational",
        ["SeverityRisky"] = "risky",
        ["StateOn"] = "on",
        ["StateOff"] = "off",
        ["DebloatUwpHeader"] = "Installed UWP apps",
        ["DebloatScanBtn"] = "Scan",
        ["DebloatScanningBtn"] = "Scanning…",
        ["DebloatRemoveBtn"] = "Remove",
        ["AppsWingetHeader"] = "winget catalog",
        ["AppsWingetMissing"] = "winget not found",
        ["AppsInstallBtn"] = "Install",
        ["AppsInstallingBtn"] = "Installing…",
        ["AppsInstalledBtn"] = "Done",
        ["AppsRetryBtn"] = "Retry",
        ["AppsRuntimeBundleBtn"] = "Install VC++ redist + .NET Desktop Runtime",
        ["AppsLogHeader"] = "Install log",
        ["ScriptRunBtn"] = "Run",
        ["ScriptRunningBtn"] = "Running…",
        ["CleanActionsHeader"] = "One-off actions",
        ["DiagDevicesHeader"] = "Problem devices",
        ["DiagDevicesEmpty"] = "No problem devices found.",
        ["DiagSearchBtn"] = "Search online",
        ["DiagQuickLinksHeader"] = "Quick links",
        ["DiagDeviceManagerBtn"] = "Device Manager",
        ["DiagSystemRestoreBtn"] = "System Restore",
        ["DiagControlPanelBtn"] = "Control Panel",
        ["DiagSystemPropertiesBtn"] = "System Properties",
        ["DiagProgramsFeaturesBtn"] = "Programs and Features"
    };

    public string this[string key] => (_currentLanguage == "en" ? En : Ru).TryGetValue(key, out var value) ? value : key;

    public void SetLanguage(string languageCode)
    {
        var normalized = languageCode.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
        if (normalized == _currentLanguage)
            return;

        _currentLanguage = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
