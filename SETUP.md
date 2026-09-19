# Настройка Analyze-YouTube.ps1 (Windows)

Чек-лист разворачивания локальной системы анализа YouTube-видео:
yt-dlp → PowerShell (очистка субтитров) → Anthropic Claude (анализ) →
Notion (база фактуры).

## 1. Установка yt-dlp

Вариант A — через winget (рекомендуется, Windows 10/11):

```powershell
winget install yt-dlp.yt-dlp
```

Вариант B — через pip (если установлен Python 3.8+):

```powershell
pip install -U yt-dlp
```

Вариант C — ручная установка:

```powershell
Invoke-WebRequest -Uri "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" -OutFile "C:\Tools\yt-dlp.exe"
```

Затем добавить `C:\Tools` в переменную среды `PATH` (Панель управления →
Система → Дополнительные параметры системы → Переменные среды).

Проверка установки:

```powershell
yt-dlp --version
```

FFmpeg не требуется — скрипт скачивает только файл субтитров
(`--skip-download`), видео и аудио не загружаются.

## 2. Ключ Anthropic API

1. Зарегистрироваться/войти на https://console.anthropic.com.
2. Раздел **API Keys** → **Create Key**, задать имя (например,
   `youtube-analyzer`), скопировать значение (начинается с `sk-ant-`).
3. Пополнить баланс в разделе **Billing**, если ключ создаётся впервые
   (без баланса запросы будут отклоняться с ошибкой `insufficient credits`).
4. Сохранить ключ в переменную среды пользователя:

```powershell
[System.Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...", "User")
```

После этого перезапустить терминал/PowerShell, чтобы переменная подхватилась.

## 3. Интеграция Notion и ключ API

1. Открыть https://www.notion.so/my-integrations → **New integration**.
2. Указать имя (например, `YouTube Analyzer`), выбрать workspace, тип —
   **Internal**.
3. Во вкладке **Capabilities** оставить как минимум:
   - Read content
   - Insert content
4. Скопировать **Internal Integration Secret** (начинается с `ntn_` или
   `secret_`) и сохранить в переменную среды:

```powershell
[System.Environment]::SetEnvironmentVariable("NOTION_API_KEY", "ntn_...", "User")
```

## 4. Привязка интеграции к базе данных и получение Database ID

1. Создать в Notion базу данных (Table) со следующими полями (типы важны —
   имена свойств должны совпадать буквально, скрипт обращается к ним по
   названию):

   | Название свойства     | Тип Notion   |
   |------------------------|--------------|
   | Название               | Title        |
   | Главный тезис          | Text         |
   | Фактура и аргументы    | Text         |
   | Цитаты                 | Text         |
   | Боли аудитории         | Text         |
   | Углы подачи            | Multi-select |

2. Открыть базу данных как страницу (не inline-таблицу) → кнопка **⋯** в
   правом верхнем углу → **Connections** (или **Add connections**) →
   выбрать созданную интеграцию `YouTube Analyzer`. Без этого шага API
   вернёт `object_not_found`, даже если ключ верный.
3. Скопировать ID базы данных из её URL:

   ```
   https://www.notion.so/workspace/<DATABASE_ID>?v=<view_id>
   ```

   `DATABASE_ID` — это 32 символа (иногда с дефисами) сразу после имени
   workspace и слэша, до символа `?`.
4. Сохранить в переменную среды:

```powershell
[System.Environment]::SetEnvironmentVariable("NOTION_DATABASE_ID", "<DATABASE_ID>", "User")
```

## 5. Проверка переменных среды

Открыть новое окно PowerShell и выполнить:

```powershell
$env:ANTHROPIC_API_KEY
$env:NOTION_API_KEY
$env:NOTION_DATABASE_ID
```

Все три должны вывести непустые значения.

## 6. Запуск

```powershell
.\Analyze-YouTube.ps1 -Url "https://www.youtube.com/watch?v=XXXXXXXXXXX"
```

Дополнительные параметры:

- `-SubtitleLang "en"` — если у видео нет русских автосубтитров.
- `-KeepArtifacts` — сохранить скачанный `.vtt` и сырой ответ модели во
  временной папке (`%TEMP%\yt-analyze-...`) для отладки.
- `-AnthropicModel "claude-3-5-sonnet-20241022"` — зафиксировать
  конкретную версию модели вместо алиаса `-latest`.

## Типичные ошибки

- **`yt-dlp завершился с ошибкой`** — видео приватное/удалено/регион
  заблокирован. Проверить URL напрямую в браузере.
- **`Субтитры не найдены`** — у видео нет автоматических субтитров для
  указанных языков. Попробовать `-SubtitleLang "en"` или другой язык.
- **`401` от Anthropic** — неверный или истёкший `ANTHROPIC_API_KEY`.
- **`object_not_found` от Notion** — интеграция не подключена к базе
  данных (см. шаг 4.2) или неверный `NOTION_DATABASE_ID`.
- **`validation_error` от Notion про свойство** — названия или типы
  колонок в базе не совпадают с таблицей из шага 4.1.
