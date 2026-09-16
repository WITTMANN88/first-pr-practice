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
        ["AboutLicense"] = "MIT License",
        ["AboutFuture"] = "Ссылки на репозиторий и канал появятся здесь позже",
        ["HomePlaceholder"] = "Главная (пресеты, сводка, применение) — следующий проход по стилизации, не в этом шаге.",
        ["TweakListEmpty"] = "Для этой вкладки пока нет твиков в загруженном реестре.",
        ["SeveritySafe"] = "безопасно",
        ["SeveritySituational"] = "по ситуации",
        ["SeverityRisky"] = "риск"
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
        ["AboutLicense"] = "MIT License",
        ["AboutFuture"] = "Links to the repository and channel will appear here later",
        ["HomePlaceholder"] = "Home (presets, summary, apply) — a later styling pass, not this step.",
        ["TweakListEmpty"] = "No tweaks for this tab in the loaded registry yet.",
        ["SeveritySafe"] = "safe",
        ["SeveritySituational"] = "situational",
        ["SeverityRisky"] = "risky"
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
