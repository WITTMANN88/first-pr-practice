# STAKEOUT

Оконное приложение для глубокой оптимизации Windows 10/11.

- **Стек:** C# / WPF, .NET 8 (LTS)
- **Права:** встроенный манифест `requireAdministrator` (UAC при запуске)
- **Сборка:** single-file, self-contained (win-x64)
- **Логи:** зашифрованный файл в `%TEMP%\STAKEOUT` (AES-256-GCM, формат `[Время] [Действие] [Статус]`)

## Сборка и запуск

Только Windows x64 (приложение обращается к WMI, реестру, службам, PowerShell).

```powershell
# восстановление и обычная сборка (для разработки)
dotnet build

# готовый single-file .exe (self-contained)
dotnet publish -c Release
# → bin\Release\net8.0-windows\win-x64\publish\STAKEOUT.exe
```

> `EnableWindowsTargeting=true` в проекте позволяет компилировать проект и на
> Linux/CI, но запускается приложение только под Windows.

## Архитектура (модули)

| Слой | Файлы | Назначение |
|------|-------|-----------|
| Ядро | `Core/` | Логгер (шифрование), MVVM-база, команды, конвертеры, тосты, проверка прав |
| Модели | `Models/` | DTO системной информации, твиков, UWP, ПО |
| Сбор данных | `Services/SystemInfoService.cs` | CPU/температура (LibreHardwareMonitor), плата/ОЗУ/диски/GPU (WMI), сборка Windows (реестр) |
| Твики реестра | `Services/TweakService.cs`, `Tweaks.cs`, `RegistryHelper.cs`, `TweakStateStore.cs` | Каталог твиков; каждый твик обратим (захват исходных значений → откат) |
| Блокировка Яндекс | `Services/YandexBlockService.cs` | Политика `DisallowRun` |
| Модуль UWP | `Services/UwpService.cs` | `Get-AppxPackage` / `Remove-AppxPackage`, защита критичных пакетов |
| Установка ПО | `Services/SoftwareInstallService.cs`, `DownloadService.cs` | winget (`--interactive`) + прямые ссылки с Progress Bar |
| UI | `Views/`, `MainWindow.xaml`, `Themes/Styles.xaml` | Кастомный TitleBar, боковое меню, страницы, анимации |
| ViewModels | `ViewModels/` | Логика страниц (MVVM) |

## Откат изменений

Все изменения реестра и политик захватывают исходное значение в
`%ProgramData%\STAKEOUT\tweak-state.json` перед записью. Кнопка «Отменить все»
(двойной клик, внизу бокового меню) восстанавливает все применённые твики,
даже после перезапуска приложения.

## Требует настройки

Прямые ссылки на **ISLC** и **MakuTweaker** в
`Services/SoftwareInstallService.cs` заданы плейсхолдерами
(`REPLACE_WITH_...`). Подставьте фактические URL — до этого карточки покажут
«Ссылка не настроена».

## Категории твиков

Приватность/телеметрия (DiagTrack, Advertising ID, Cortana), безопасность
(UAC, BitLocker через WMI), система (драйверы, гибернация, анимации,
MenuShowDelay), производительность (MPO, Power Throttling, HAGS,
NetworkThrottlingIndex, план электропитания, USB, опрос мыши), игры
(GameDVR/Xbox, Game Mode, акселерация мыши), блокировка Яндекс.
Деструктивные действия (UAC, BitLocker, MPO) требуют подтверждения.
