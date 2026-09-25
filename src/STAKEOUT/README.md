# STAKEOUT

Оконное приложение для глубокой оптимизации Windows 10/11.

- **Стек:** C# / WPF, .NET 8 (LTS)
- **Права:** встроенный манифест `requireAdministrator` (UAC при запуске)
- **Сборка:** single-file, self-contained (win-x64)
- **Логи:** зашифрованный файл в `%TEMP%\STAKEOUT` (AES-256-GCM, формат `[Время] [Действие] [Статус]`)

## Сборка и запуск

Только Windows x64 (приложение обращается к WMI, реестру, службам, PowerShell).

```powershell
# (опционально) обновить/перекачать шрифт заголовков — уже лежит в Assets/Fonts
powershell -ExecutionPolicy Bypass -File .\prepare-assets.ps1

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
| UI | `Views/`, `MainWindow.xaml`, `Themes/Styles.xaml` | Кастомный TitleBar, боковое меню, страницы, анимации, модальное окно логов |
| Контролы | `Controls/ShimmerOverlay`, `Controls/SkeletonBlock` | Skeleton-заглушки с бегущим бликом (анимация только пока видимы) |
| Ресурсы | `Themes/Logos.xaml`, `Assets/Fonts/`, `prepare-assets.ps1` | Векторные логотипы ПО (DrawingImage), встроенный шрифт Cinzel |
| ViewModels | `ViewModels/` | Логика страниц (MVVM) |

## Шрифт заголовков (Cinzel)

Встроен в exe как WPF `<Resource>` (`Assets/Fonts/*.ttf`) и подключается через
`pack://application:,,,/Assets/Fonts/#Cinzel` с fallback на Constantia/Georgia —
установка шрифта в систему не нужна.

- `prepare-assets.ps1` скачивает Cinzel Regular/Bold и `OFL.txt` из
  [NDISCOVER/Cinzel](https://github.com/NDISCOVER/Cinzel), закреплённого на
  конкретном коммите, и проверяет SHA-256 каждого файла; файл с неверным хешем
  отклоняется. Повторный запуск пропускает валидные файлы, `-Force` — перекачать.
- Если шрифта нет, сборка выдаёт предупреждение `WarnMissingFonts`.
- Лицензия: SIL Open Font License 1.1 (`Assets/Fonts/OFL.txt`).

## Просмотр логов

Кнопка «Логи» внизу бокового меню открывает модальное окно внутри приложения:
лог из `%TEMP%\STAKEOUT` расшифровывается на лету (в фоне), статусы подсвечены
(ошибки/`ACCESS_DENIED`/`TIMEOUT` — красным), список виртуализирован (показ до
5000 последних строк). «Скопировать в буфер» копирует весь расшифрованный лог.
Закрытие — кнопкой, `Esc` или кликом по затемнённому фону.

## Откат изменений

Все изменения реестра и политик захватывают исходное значение в
`%ProgramData%\STAKEOUT\tweak-state.json` перед записью. Кнопка «Отменить все»
(двойной клик, внизу бокового меню) восстанавливает все применённые твики,
даже после перезапуска приложения.

## Прямые ссылки

**ISLC** (v1.0.4.7) и **MakuTweaker** (5.7.3) скачиваются по прямым ссылкам,
заданным в `Services/SoftwareInstallService.cs`. При выходе новых версий
обновите URL и имя файла там же.

## Категории твиков

Приватность/телеметрия (DiagTrack, Advertising ID, Cortana), безопасность
(UAC, BitLocker через WMI), система (драйверы, гибернация, анимации,
MenuShowDelay), производительность (MPO, Power Throttling, HAGS,
NetworkThrottlingIndex, план электропитания, USB, опрос мыши), игры
(GameDVR/Xbox, Game Mode, акселерация мыши), блокировка Яндекс.
Деструктивные действия (UAC, BitLocker, MPO) требуют подтверждения.
