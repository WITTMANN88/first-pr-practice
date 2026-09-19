#Requires -Version 5.1
<#
    Analyze-YouTube.ps1

    Скачивает автосубтитры YouTube-видео через yt-dlp, очищает их от
    таймкодов/VTT-разметки, отправляет транскрипт в Anthropic Claude для
    структурного анализа и создаёт страницу в базе данных Notion с
    результатом.

    Требуемые переменные среды:
        ANTHROPIC_API_KEY   - ключ Anthropic API
        NOTION_API_KEY      - Internal Integration Secret Notion
        NOTION_DATABASE_ID  - ID базы данных Notion (32 hex-символа)

    Пример запуска:
        .\Analyze-YouTube.ps1 -Url "https://www.youtube.com/watch?v=XXXXXXXXXXX"
        .\Analyze-YouTube.ps1 -Url "..." -SubtitleLang en -KeepArtifacts
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://')]
    [string]$Url,

    # Приоритет языков автосубтитров (через запятую для yt-dlp)
    [string]$SubtitleLang = 'ru,en',

    [string]$AnthropicModel = 'claude-3-5-sonnet-latest',

    [int]$AnthropicMaxTokens = 4096,

    [int]$ApiTimeoutSec = 120,

    # Не удалять временные файлы (субтитры, сырой ответ модели) после работы
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'

# --------------------------------------------------------------------------
# 0. Конфигурация и проверка окружения
# --------------------------------------------------------------------------

$AnthropicApiKey  = $env:ANTHROPIC_API_KEY
$NotionApiKey     = $env:NOTION_API_KEY
$NotionDatabaseId = $env:NOTION_DATABASE_ID

if ([string]::IsNullOrWhiteSpace($AnthropicApiKey)) {
    throw "Переменная среды ANTHROPIC_API_KEY не задана. Смотрите SETUP.md."
}
if ([string]::IsNullOrWhiteSpace($NotionApiKey)) {
    throw "Переменная среды NOTION_API_KEY не задана. Смотрите SETUP.md."
}
if ([string]::IsNullOrWhiteSpace($NotionDatabaseId)) {
    throw "Переменная среды NOTION_DATABASE_ID не задана. Смотрите SETUP.md."
}

if (-not (Get-Command yt-dlp -ErrorAction SilentlyContinue)) {
    throw "yt-dlp не найден в PATH. Установите его (см. SETUP.md) перед запуском скрипта."
}

$SystemPrompt = @"
Ты — системный аналитик контента. Твоя задача — извлечь факты и структуру из транскрипта видео. Правила: строгая логика, без выдумок, лаконичность.
Алгоритм:
1. Сформулируй главную идею (1-2 предложения).
2. Выдели фактуру (смысловые блоки: тезис + конкретные факты/цифры).
3. Выяви боли аудитории.
4. Предложи 3 угла подачи для постов.
Выведи результат СТРОГО в формате JSON без markdown-обертки. Ключи: title, core_thesis, facts, quotes, pain_points, angles.
"@

$workDir = Join-Path $env:TEMP ("yt-analyze-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

# --------------------------------------------------------------------------
# Вспомогательные функции
# --------------------------------------------------------------------------

function ConvertFrom-VttToPlainText {
    <#
        Строгая очистка VTT-субтитров: убирает заголовок WEBVTT, метаданные,
        строки с таймкодами, inline-теги <c>/<00:00:00.000> и схлопывает
        повторяющиеся строки, характерные для rolling-автосубтитров YouTube.
    #>
    param([Parameter(Mandatory)][string]$RawContent)

    $lines = $RawContent -split '\r?\n'

    $cleaned = New-Object System.Collections.Generic.List[string]
    $previous = $null

    foreach ($line in $lines) {
        $text = $line.Trim()

        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        if ($text -eq 'WEBVTT') { continue }
        if ($text -match '^(Kind|Language):') { continue }
        # строка с таймкодом: 00:00:00.000 --> 00:00:02.000 align:start position:0%
        if ($text -match '^\d{2}:\d{2}:\d{2}[.,]\d{3}\s*-->\s*\d{2}:\d{2}:\d{2}[.,]\d{3}') { continue }
        # порядковый номер реплики (SRT-подобные файлы)
        if ($text -match '^\d+$') { continue }

        # inline теги вида <00:00:01.234> и <c>, </c>, <c.color>
        $text = $text -replace '<\d{2}:\d{2}:\d{2}[.,]\d{3}>', ''
        $text = $text -replace '</?c[^>]*>', ''
        # прочие HTML/VTT теги
        $text = $text -replace '<[^>]+>', ''
        # HTML-сущности
        $text = $text -replace '&nbsp;', ' '
        $text = $text -replace '&amp;', '&'
        $text = $text -replace '&gt;', '>'
        $text = $text -replace '&lt;', '<'

        $text = $text.Trim()
        if ([string]::IsNullOrWhiteSpace($text)) { continue }

        # rolling-автосубтитры YouTube дублируют предыдущую строку целиком
        if ($text -eq $previous) { continue }

        $cleaned.Add($text)
        $previous = $text
    }

    $joined = ($cleaned -join ' ') -replace '\s{2,}', ' '
    return $joined.Trim()
}

function Split-ForNotionRichText {
    <#
        Notion ограничивает длину content одного rich_text-блока 2000
        символами. Разбивает строку на массив таких блоков.
    #>
    param([string]$Text, [int]$ChunkSize = 1900)

    if ([string]::IsNullOrEmpty($Text)) { return @() }

    $chunks = @()
    for ($i = 0; $i -lt $Text.Length; $i += $ChunkSize) {
        $len = [Math]::Min($ChunkSize, $Text.Length - $i)
        $chunks += $Text.Substring($i, $len)
    }

    return $chunks | ForEach-Object {
        @{ type = 'text'; text = @{ content = $_ } }
    }
}

function ConvertTo-BulletedText {
    <#
        Приводит значение поля (строка, массив строк или массив объектов
        thesis/details) к тексту в виде маркированного списка.
    #>
    param($Value)

    if ($null -eq $Value) { return '' }

    $lines = @()
    foreach ($item in @($Value)) {
        if ($item -is [string]) {
            $lines += "• $item"
        }
        elseif ($item -is [System.Collections.IDictionary] -or $item.PSObject.Properties.Count -gt 0) {
            $parts = $item.PSObject.Properties | ForEach-Object { "$($_.Value)" }
            $lines += "• " + ($parts -join ' — ')
        }
        else {
            $lines += "• $item"
        }
    }

    return ($lines -join "`n")
}

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [string]$OperationName = 'операция',
        [int]$MaxAttempts = 3
    )

    $attempt = 0
    while ($true) {
        $attempt++
        try {
            return & $Action
        }
        catch {
            $detail = $_.Exception.Message
            if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
                $detail = $_.ErrorDetails.Message
            }

            if ($attempt -ge $MaxAttempts) {
                throw "Не удалось выполнить '$OperationName' после $attempt попыток: $detail"
            }

            $delay = [Math]::Pow(2, $attempt)
            Write-Warning "[$OperationName] попытка $attempt не удалась ($detail). Повтор через $delay сек..."
            Start-Sleep -Seconds $delay
        }
    }
}

# --------------------------------------------------------------------------
# 1. Скачивание автосубтитров через yt-dlp (без видео)
# --------------------------------------------------------------------------

Write-Host "==> Скачиваю автосубтитры для: $Url"

$ytDlpArgs = @(
    '--skip-download',
    '--write-auto-sub',
    '--sub-lang', $SubtitleLang,
    '--sub-format', 'vtt',
    '--output', (Join-Path $workDir '%(id)s.%(ext)s'),
    '--no-warnings',
    $Url
)

$ytDlpOutput = & yt-dlp @ytDlpArgs 2>&1
$ytDlpExitCode = $LASTEXITCODE

if ($ytDlpExitCode -ne 0) {
    Write-Host $ytDlpOutput
    throw "yt-dlp завершился с ошибкой (код $ytDlpExitCode). Проверьте URL и доступность видео."
}

$vttFile = Get-ChildItem -Path $workDir -Filter '*.vtt' | Select-Object -First 1

if (-not $vttFile) {
    throw "Субтитры не найдены. У видео отсутствуют автосубтитры для языков '$SubtitleLang', либо видео недоступно."
}

Write-Host "==> Найден файл субтитров: $($vttFile.Name)"

# --------------------------------------------------------------------------
# 2. Очистка субтитров от таймкодов и разметки
# --------------------------------------------------------------------------

$rawVtt = Get-Content -Path $vttFile.FullName -Raw -Encoding UTF8
$transcript = ConvertFrom-VttToPlainText -RawContent $rawVtt

if ([string]::IsNullOrWhiteSpace($transcript)) {
    throw "После очистки субтитров получен пустой текст. Файл субтитров: $($vttFile.FullName)"
}

Write-Host "==> Транскрипт очищен (длина: $($transcript.Length) символов)"

# --------------------------------------------------------------------------
# 3. Анализ через Anthropic Claude API
# --------------------------------------------------------------------------

Write-Host "==> Отправляю транскрипт в Anthropic API (модель: $AnthropicModel)"

$anthropicBody = @{
    model      = $AnthropicModel
    max_tokens = $AnthropicMaxTokens
    system     = $SystemPrompt
    messages   = @(
        @{ role = 'user'; content = $transcript }
    )
} | ConvertTo-Json -Depth 10

$anthropicHeaders = @{
    'x-api-key'         = $AnthropicApiKey
    'anthropic-version' = '2023-06-01'
    'content-type'      = 'application/json'
}

$anthropicResponse = Invoke-WithRetry -OperationName 'Anthropic API' -Action {
    Invoke-RestMethod -Uri 'https://api.anthropic.com/v1/messages' `
        -Method Post `
        -Headers $anthropicHeaders `
        -Body $anthropicBody `
        -TimeoutSec $ApiTimeoutSec
}

$modelText = $anthropicResponse.content[0].text

if ($KeepArtifacts) {
    $modelText | Out-File -FilePath (Join-Path $workDir 'claude-response.json') -Encoding UTF8
}

# На случай если модель всё же обернула ответ в ```json ... ```
$jsonText = $modelText.Trim() -replace '^```(json)?', '' -replace '```$', ''
$jsonText = $jsonText.Trim()

try {
    $analysis = $jsonText | ConvertFrom-Json
}
catch {
    throw "Не удалось разобрать JSON-ответ модели: $($_.Exception.Message)`nОтвет модели:`n$modelText"
}

foreach ($requiredKey in 'title', 'core_thesis', 'facts', 'quotes', 'pain_points', 'angles') {
    if (-not ($analysis.PSObject.Properties.Name -contains $requiredKey)) {
        throw "В ответе модели отсутствует обязательное поле '$requiredKey'."
    }
}

Write-Host "==> Анализ получен: $($analysis.title)"

# --------------------------------------------------------------------------
# 4. Создание страницы в Notion
# --------------------------------------------------------------------------

Write-Host "==> Создаю страницу в Notion..."

$angleNames = @($analysis.angles) | Where-Object { $_ } | ForEach-Object {
    $name = "$_"
    if ($name.Length -gt 100) { $name = $name.Substring(0, 100) }
    @{ name = $name }
}

$notionPageBody = @{
    parent     = @{ database_id = $NotionDatabaseId }
    properties = @{
        'Название'          = @{
            title = @(
                @{ type = 'text'; text = @{ content = "$($analysis.title)" } }
            )
        }
        'Главный тезис'     = @{
            rich_text = Split-ForNotionRichText -Text "$($analysis.core_thesis)"
        }
        'Фактура и аргументы' = @{
            rich_text = Split-ForNotionRichText -Text (ConvertTo-BulletedText -Value $analysis.facts)
        }
        'Цитаты'            = @{
            rich_text = Split-ForNotionRichText -Text (ConvertTo-BulletedText -Value $analysis.quotes)
        }
        'Боли аудитории'    = @{
            rich_text = Split-ForNotionRichText -Text (ConvertTo-BulletedText -Value $analysis.pain_points)
        }
        'Углы подачи'       = @{
            multi_select = $angleNames
        }
    }
} | ConvertTo-Json -Depth 12

$notionHeaders = @{
    'Authorization'  = "Bearer $NotionApiKey"
    'Notion-Version' = '2022-06-28'
    'Content-Type'   = 'application/json'
}

$notionResponse = Invoke-WithRetry -OperationName 'Notion API' -Action {
    Invoke-RestMethod -Uri 'https://api.notion.com/v1/pages' `
        -Method Post `
        -Headers $notionHeaders `
        -Body $notionPageBody `
        -TimeoutSec $ApiTimeoutSec
}

Write-Host "==> Готово. Страница создана: $($notionResponse.url)"

# --------------------------------------------------------------------------
# 5. Уборка временных файлов
# --------------------------------------------------------------------------

if (-not $KeepArtifacts) {
    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
else {
    Write-Host "==> Временные файлы сохранены в: $workDir"
}
