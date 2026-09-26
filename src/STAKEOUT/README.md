# STAKEOUT

Оконное приложение для глубокой оптимизации Windows 10/11.

- **Стек:** C# / WPF, .NET 8 (LTS)
- **Права:** встроенный манифест `requireAdministrator` (UAC при запуске)
- **Сборка:** single-file, self-contained (win-x64)
- **Логи:** зашифрованный файл в `%TEMP%\STAKEOUT` (AES-256-GCM, формат `[Время] [Действие] [Статус]`)

## Структура решения

```
src/
  STAKEOUT.sln
  STAKEOUT/         WPF-приложение (net8.0-windows): UI, WMI, реальный реестр, PowerShell
  STAKEOUT.Core/    ядро без привязки к ОС (net8.0): шифрование лога, движок отката,
                    tweak-state.json, каталог UWP, уведомления, локализованные строки
  STAKEOUT.Tests/   xUnit-тесты ядра (net8.0) — запускаются на любой ОС
```

Windows-зависимое (реестр, диалоги, UI-поток) скрыто за интерфейсами ядра
(`IRegistryAccess`, `IDialogService`, `IUiDispatcher`), поэтому логику отката и
хранения можно проверять без Windows — на in-memory реестре.

## Сборка, тесты, запуск

Приложение запускается только под Windows x64 (WMI, реестр, службы, PowerShell).
Сборка и тесты работают и на Linux/CI (`EnableWindowsTargeting=true`).

```powershell
cd src

# сборка всего решения
dotnet build STAKEOUT.sln

# юнит-тесты ядра (шифрование, откат, tweak-state.json, UWP, уведомления, локализация)
dotnet test STAKEOUT.sln

# готовый single-file .exe (self-contained, ядро и переводы внутри)
dotnet publish STAKEOUT -c Release
# → STAKEOUT\bin\Release\net8.0-windows\win-x64\publish\STAKEOUT.exe

# (опционально) перекачать шрифт заголовков — уже лежит в STAKEOUT/Assets/Fonts
powershell -ExecutionPolicy Bypass -File .\STAKEOUT\prepare-assets.ps1
```

## Архитектура (модули)

| Слой | Где | Назначение |
|------|-----|-----------|
| Шифрование лога | `Core/Logging/` | `LogCipher` (AES-256-GCM), `EncryptedLogFile`, фасад `Logger` |
| Движок отката | `Core/Registry/` | `RegistryRollback` (захват → запись → восстановление, транзакционно), кодеки значений |
| Хранилище отката | `Core/Persistence/TweakStateStore.cs` | `tweak-state.json`: версия, атомарная запись, карантин повреждённого файла, потокобезопасность |
| Каталог UWP | `Core/Uwp/` | Категории (мусор, игры, мультимедиа, утилиты, системные…), защита критичных, парсер вывода PowerShell |
| Уведомления | `Core/Notifications/` | `INotificationService` (отправка), `INotificationFeed` (показ), `NotificationService` |
| Локализация | `Core/Localization/` | `Strings.resx` (русский, по умолчанию), `Strings.en.resx`, `LocalizationManager` |
| Сбор данных | `STAKEOUT/Services/SystemInfoService.cs` | CPU/температура (LibreHardwareMonitor), плата/ОЗУ/диски/GPU (WMI), сборка Windows (реестр) |
| Твики | `STAKEOUT/Services/TweakService.cs`, `Tweaks.cs` | Каталог твиков поверх движка отката |
| Реестр Windows | `STAKEOUT/Services/RegistryHelper.cs` | Реальный реестр за `IRegistryAccess` |
| UWP / Яндекс / ПО | `STAKEOUT/Services/` | PowerShell, `DisallowRun`, winget + прямые ссылки |
| Композиция | `STAKEOUT/Infrastructure/` | DI-контейнер (`ServiceRegistration`), WPF-реализации интерфейсов ядра |
| UI | `STAKEOUT/Views/`, `MainWindow.xaml`, `Themes/`, `Controls/` | Окно, страницы, стили, логотипы, shimmer |
| ViewModels | `STAKEOUT/ViewModels/` | MVVM; зависимости только через конструктор |

## Уведомления (MVVM)

ViewModel получает `INotificationService` через конструктор и вызывает
`_notify.Success(...)`, `.Error(...)`, `.Warning(...)`, `.Info(...)` — без
code-behind и без обращения к окну. Оболочка отображает `INotificationFeed.Active`:
тост живёт 5 с, одновременно не больше 4 (старые вытесняются), изменения
коллекции всегда идут через UI-поток, каждое уведомление пишется в лог.
Подтверждения деструктивных действий — через `IDialogService`.

## Локализация

Все строки интерфейса — в `STAKEOUT.Core/Localization/Strings.resx` (русский —
язык по умолчанию). Из кода: `Strings.Key`, из XAML: `{x:Static loc:Strings.Key}` —
опечатка в ключе ломает сборку, а не интерфейс.

- Язык: аргумент `--lang en` / `--lang=en`, затем переменная `STAKEOUT_LANG`,
  иначе русский. Применяется при старте (смена — при следующем запуске).
- **Добавить язык** = положить `Strings.<код>.resx` рядом (например `Strings.de.resx`).
  Код менять не нужно: доступность языка определяется по наличию сателлитной сборки.
- Тесты `ResourceParityTests` не дают разойтись переводам: одинаковый набор
  ключей, нет пустых значений, совпадают плейсхолдеры `{0}`, каждая строка
  форматируется без ошибок.

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

Перед любой записью в реестр исходное значение (или факт его отсутствия)
сохраняется в `%ProgramData%\STAKEOUT\tweak-state.json`. Кнопка «Отменить все»
(двойной клик, внизу бокового меню) восстанавливает все применённые твики,
даже после перезапуска приложения.

- Применение транзакционно: если одна из записей твика не удалась, уже
  сделанные записи этого твика откатываются.
- Если откат прошёл частично, запись о твике сохраняется — откат можно повторить.
- Файл пишется атомарно; повреждённый файл переименовывается в
  `*.corrupt-<время>` (данные не теряются), приложение продолжает работу.

## Прямые ссылки

**ISLC** (v1.0.4.7) и **MakuTweaker** (5.7.3) скачиваются по прямым ссылкам,
заданным в `Services/SoftwareInstallService.cs`. При выходе новых версий
обновите URL и имя файла там же.

## Категории UWP

Предустановленный мусор, игры, мультимедиа, утилиты, сторонние, прочие Microsoft,
системные. «Выбрать всё (только мусор)» отмечает только предустановленный мусор;
защищённые пакеты (Store, Калькулятор, App Installer, runtime-библиотеки)
всегда получают категорию «Системные» и не удаляются.

## Категории твиков

Приватность/телеметрия (DiagTrack, Advertising ID, Cortana), безопасность
(UAC, BitLocker через WMI), система (драйверы, гибернация, анимации,
MenuShowDelay), производительность (MPO, Power Throttling, HAGS,
NetworkThrottlingIndex, план электропитания, USB, опрос мыши), игры
(GameDVR/Xbox, Game Mode, акселерация мыши), блокировка Яндекс.
Деструктивные действия (UAC, BitLocker, MPO) требуют подтверждения.
