#Requires -Version 5.1
<#
.SYNOPSIS
    Установка связки MCP для моддинга Arma Reforger (Enfusion) и Blender MCP в Claude Code.

.DESCRIPTION
    Ставит и регистрирует в Claude Code (scope: user):
      enfusion-mcp    - SyykoNT/enfusion-mcp-SyykoNT: создание модов под Reforger 1.9, компиляция, тестовый сервер, Workbench
      enfusion-audit  - Goldwep/enfusion-workbench-mcp: аудит (GUID-индекс, линтер, логи, server.json)
      arga-enfusion   - ArgA-Reforger/arga_enfusion_mcp: чтение .pak модов из Workshop (только чтение)
      blender         - ahujasid/blender-mcp: Blender (телеметрия выключена, safe mode включён)
      obsidian-vault  - bitbonsai/mcpvault: заметки Obsidian прямо в файлах хранилища (Obsidian может быть закрыт)
      obsidian        - встроенный MCP плагина Obsidian "Local REST API" (только с -WithObsidian)
      substance-painter - elliezu/SubstancePainterMCP: текстуры в Substance 3D Painter 12.1+
      discord         - npm @discord-mcp/cli 0.34.1 (cappyeo/discord-mcp): свой Discord-сервер, только с -WithDiscord;
                        по умолчанию изменения запрещены (preview), удаление - только пробный прогон
    Skills (в %USERPROFILE%\.claude\skills, их же читает Cursor):
      arma-reforger-modding (Mavericktfius), arma-reforger + arma-reforger-server-config +
      arma-reforger-server-hosting (Reidond/stavka), reforger-obsidian-workflow (свой, из этого скрипта),
      официальные obsidianmd/obsidian-skills, discord-bot-stinger (legioncodeinc/that-git-life).
    Плюс копия Workshop-модов для аудита, запреты в %USERPROFILE%\.claude\settings.json
    (запись/Workbench для enfusion-audit, удаление заметок и команды Obsidian) и, с -WithCursor,
    те же MCP-серверы в %USERPROFILE%\.cursor\mcp.json.

    Работает и без CLI claude (только десктоп-приложение): тогда серверы пишутся напрямую
    в %USERPROFILE%\.claude.json, который читает вкладка Code в Claude Desktop.

    Весь код берётся по зафиксированным коммитам, которые прошли ручной аудит 2026-10-28
    (SyykoNT, mcpvault, obsidian-skills, Reidond - 2026-11-17). Индекс API: SyykoNT - Reforger 1.9.0,
    enfusion-audit - 1.8 (его проверки работают по файлам и от версии игры почти не зависят).
    Зависимости ставятся строго по lock-файлам (npm ci / uv --frozen / cargo --locked),
    install-скрипты npm отключены (кроме нативного better-sqlite3).

.NOTES
    Запуск (обычный пользователь, НЕ администратор):
        Unblock-File .\Install-ReforgerMcp.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-ReforgerMcp.ps1
    Файл должен оставаться в кодировке UTF-8 с BOM, иначе Windows PowerShell 5.1 испортит русские строки.
    Повторный запуск безопасен: уже собранные компоненты пропускаются.
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = 'C:\mcp',
    [string]$AuditRoot,
    [string]$ToolsPath,
    [string]$GamePath,
    [string]$WorkshopSource,
    [string]$ServerLogsPath,
    [switch]$InstallPrereqs,
    [switch]$SkipArga,
    [switch]$SkipBlender,
    [switch]$SkipSkill,
    [switch]$SkipWorkshopCopy,
    [switch]$SkipPermissions,
    [switch]$SkipObsidianSkills,
    [string]$ObsidianVault,
    [switch]$ObsidianReadOnly,
    [switch]$SkipObsidianVault,
    [switch]$WithObsidian,
    [ValidateRange(1, 65535)][int]$ObsidianPort = 27123,
    [switch]$WithCursor,
    [string]$AssetsRoot,
    [switch]$SkipPainter,
    [switch]$WithDiscord,
    [string]$DiscordGuildId,
    [switch]$DiscordAllowWrites,
    [switch]$SkipDiscordSkill
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---------------------------------------------------------------------------
# Проверенные версии. Менять только после повторного аудита кода.
# ---------------------------------------------------------------------------
$Pins = @{
    Creator = @{ Url = 'https://github.com/SyykoNT/enfusion-mcp-SyykoNT';       Commit = '5ab01e735c466b3ebe434098acf38ea425967140' } # v0.16.0, Reforger 1.9
    Audit   = @{ Url = 'https://github.com/Goldwep/enfusion-workbench-mcp';      Commit = '1dd8b28f820175d845bc0977cbea3bfd4b913e1a' } # 1.0.0
    Arga    = @{ Url = 'https://github.com/ArgA-Reforger/arga_enfusion_mcp';     Commit = '1308786cc51df48c6a62dea4fb65e24c94020086' } # 0.1.0
    Tools   = @{ Url = 'https://github.com/landaire/enfusion_tools';             Commit = 'bb325b0cfb0c69cd44bfaeb1f365ea6624469192' } # enfusion CLI
    Blender = @{ Url = 'https://github.com/ahujasid/blender-mcp';                Commit = 'febcd90ece59c3416d7c55fd3723cea46bcaa59d' } # 2.2.4
    Skill   = @{ Url = 'https://github.com/Mavericktfius/arma-reforger-modding'; Commit = 'ac587abd571f28adbac8c7861c6034c5105975e9' }
    ObsidianSkills = @{ Url = 'https://github.com/obsidianmd/obsidian-skills';  Commit = 'caf3e98cd22d810db164f26c9a442f0a280daa43' } # plugin 1.2.0
    Vault          = @{ Url = 'https://github.com/bitbonsai/mcpvault';           Commit = '5d4e68550e9fe27fc24b9e16d96b8ff5b39355d7' } # 0.18.4
    ReidondSkills  = @{ Url = 'https://github.com/Reidond/stavka';              Commit = 'f4efa4d5f92f216182ba32aab8883e1c8ca0154c' } # GPL-3.0
    Painter        = @{ Url = 'https://github.com/elliezu/SubstancePainterMCP'; Commit = 'e382927cc1a3ea7d4bd6e48d28403d339a4613df' } # 1.0.0, MIT
    DiscordSkill   = @{ Url = 'https://github.com/legioncodeinc/that-git-life'; Commit = 'c1e05d022d865e32f4cb93d313d5f51ad0175a0b' } # AGPL-3.0
}
$VaultMinNode = [version]'22.12.0'

# Substance Painter MCP: в репозитории нет lock-файла, собираем его с отсечкой по дате
# (воспроизводимо, pip-audit чист, mcp 1.30.0 - проверено 2026-11-17).
$PainterExcludeNewer = '2026-11-16T00:00:00Z'

# Discord MCP ставится из npm (монорепозиторий на pnpm), исходники проверены на коммите
# 1f24257163af5c90c2ab1b55793fd92e9281a213 (v0.34.1), опубликованная сборка сверена с ними.
# Зависимости разрешаются с отсечкой по дате, а дерево сверяется с проверенным отпечатком
# (139 пакетов, npm audit: 0 уязвимостей).
$DiscordPackage = '@discord-mcp/cli'
$DiscordVersion = '0.34.1'
$DiscordBefore = '2026-11-16T00:00:00Z'
$DiscordTreeHash = '40a7341a3fa94c48d15243210b92f61add26f2ca8ca6ef30ac82665a675bdd27'
$DiscordMinNode = [version]'22.12.0'
# Все категории инструментов, кроме монетизации, личных сообщений (users) и внешнего поиска эмодзи (inspiration).
$DiscordCategories = @(
    'app_emojis', 'application', 'audit_log', 'automod', 'channels', 'commands', 'components_v2', 'emojis',
    'events', 'guild', 'intelligence', 'interactions', 'invites', 'members', 'messages', 'onboarding',
    'permissions', 'polls', 'reactions', 'roles', 'server', 'soundboard', 'stage_instances', 'stickers',
    'templates', 'threads', 'voice', 'webhooks'
)

# Запреты для заметок: удаление и запуск команд Obsidian (через них работают и плагины вроде Shell commands).
$ObsidianDenyRules = @(
    'mcp__obsidian-vault__delete_note',
    'mcp__obsidian__vault_delete',
    'mcp__obsidian__command_execute'
)
# uv.lock у blender-mcp содержит mcp 1.29.0 и pyjwt 2.13.0 с известными CVE. Пересобираем lock
# с отсечкой по дате: результат воспроизводим, pip-audit на нём чист (проверено 2026-10-28).
$BlenderExcludeNewer = '2026-10-27T00:00:00Z'
$BlenderMinMcp = [version]'1.30.0'

# Инструменты enfusion-audit, которые запрещаются: Workbench принадлежит enfusion-mcp,
# аудит не должен писать в проекты и запускать процессы.
$AuditDenyTools = @(
    'wb_build_data', 'wb_cleanup', 'wb_cli_run', 'wb_clipboard', 'wb_component', 'wb_connect',
    'wb_diagnose', 'wb_entity_create', 'wb_entity_delete', 'wb_entity_duplicate', 'wb_entity_inspect',
    'wb_entity_list', 'wb_entity_modify', 'wb_entity_select', 'wb_execute_action', 'wb_launch',
    'wb_layers', 'wb_localization', 'wb_open_resource', 'wb_play', 'wb_prefabs', 'wb_projects',
    'wb_reload', 'wb_resources', 'wb_save', 'wb_script_editor', 'wb_state', 'wb_stop', 'wb_terrain',
    'wb_undo_redo', 'wb_validate', 'wb_validate_scripts',
    'terrain_inspect', 'terrain_navmesh_status', 'terrain_road_export_graph', 'scenario_create',
    'mod', 'script_create', 'layout_create', 'config_create', 'server_config', 'faction_create',
    'scenario_create_conflict', 'scenario_clone_area', 'scenario_apply_template', 'game_duplicate',
    'building_setup', 'script_format', 'refactor_replace_guid', 'refactor_move_resource_path',
    'refactor_rename_project_id', 'refactor_normalize_dependencies', 'server_launch', 'server_stop'
)

# ---------------------------------------------------------------------------
# Вспомогательные функции
# ---------------------------------------------------------------------------
$Results = New-Object System.Collections.Generic.List[object]

function Write-Step([string]$Message) { Write-Host ''; Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "    [OK] $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "    [!] $Message" -ForegroundColor Yellow }

function Add-Result([string]$Component, [string]$Status, [string]$Note) {
    $Results.Add([pscustomobject]@{ Component = $Component; Status = $Status; Note = $Note })
}

function Get-FullPath([string]$Path) {
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($full.EndsWith(':')) { $full += '\' }
    return $full
}

function Resolve-Exe([string]$Name) {
    $cmd = Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    return $null
}

function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

# Запуск внешней программы: вывод идёт в консоль построчно, ненулевой код = исключение.
# EAP временно 'Continue', иначе Windows PowerShell 5.1 падает на любой строке в stderr.
function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory,
        [switch]$AllowFail,
        [switch]$Quiet,
        [switch]$HideArgs
    )
    $lines = New-Object System.Collections.Generic.List[string]
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    if ($WorkingDirectory) { Push-Location -LiteralPath $WorkingDirectory }
    try {
        & $FilePath @ArgumentList 2>&1 | ForEach-Object {
            $line = [string]$_
            $lines.Add($line)
            if (-not $Quiet) { Write-Host "    $line" -ForegroundColor DarkGray }
        }
        $code = $LASTEXITCODE
    }
    finally {
        if ($WorkingDirectory) { Pop-Location }
        $ErrorActionPreference = $prevEap
    }
    if ($code -ne 0 -and -not $AllowFail) {
        $shown = if ($HideArgs) { '(аргументы скрыты)' } else { $ArgumentList -join ' ' }
        throw ('Команда завершилась с кодом {0}: {1} {2}' -f $code, $FilePath, $shown)
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $lines.ToArray() }
}

# Клон ровно одного коммита. Чужую папку с тем же именем не трогает.
# SparsePaths: из большого репозитория скачиваются и раскладываются только эти пути.
function Get-PinnedRepo([string]$Url, [string]$Commit, [string]$Dir, [string[]]$SparsePaths = @()) {
    if (Test-Path -LiteralPath $Dir) {
        if (-not (Test-Path -LiteralPath (Join-Path $Dir '.git'))) {
            throw "Папка $Dir уже существует и не является git-репозиторием. Переименуйте её и запустите скрипт снова."
        }
        $origin = (Invoke-Native $script:Git @('-C', $Dir, 'remote', 'get-url', 'origin') -Quiet).Output | Select-Object -First 1
        $origin = ([string]$origin).Trim() -replace '\.git$', ''
        if ($origin.TrimEnd('/') -ne $Url) {
            throw "В $Dir лежит другой репозиторий ($origin), ожидался $Url."
        }
    }
    else {
        New-Item -ItemType Directory -Path $Dir -Force | Out-Null
        $null = Invoke-Native $script:Git @('-C', $Dir, 'init', '--quiet') -Quiet
        $null = Invoke-Native $script:Git @('-C', $Dir, 'remote', 'add', 'origin', $Url) -Quiet
    }
    # Файлы должны совпадать с проверенными байт в байт: без конвертации LF -> CRLF.
    $null = Invoke-Native $script:Git @('-C', $Dir, 'config', 'core.autocrlf', 'false') -Quiet
    $fetchArgs = @('-C', $Dir, 'fetch', '--depth', '1', '--quiet')
    if ($SparsePaths.Count -gt 0) {
        $null = Invoke-Native $script:Git @('-C', $Dir, 'config', 'remote.origin.promisor', 'true') -Quiet
        $null = Invoke-Native $script:Git @('-C', $Dir, 'config', 'remote.origin.partialclonefilter', 'blob:none') -Quiet
        $null = Invoke-Native $script:Git (@('-C', $Dir, 'sparse-checkout', 'set', '--no-cone') + $SparsePaths) -Quiet
        $fetchArgs += '--filter=blob:none'
    }
    $null = Invoke-Native $script:Git ($fetchArgs + @('origin', $Commit))
    $null = Invoke-Native $script:Git @('-C', $Dir, 'checkout', '--force', '--quiet', '--detach', 'FETCH_HEAD')
    $head = ([string]((Invoke-Native $script:Git @('-C', $Dir, 'rev-parse', 'HEAD') -Quiet).Output | Select-Object -First 1)).Trim()
    if ($head -ne $Commit) { throw "В $Dir получен коммит $head вместо $Commit." }
    Write-Ok "$Url @ $($Commit.Substring(0, 12))"
}

# Папка - рабочий клон именно этого репозитория (а не чужая папка, сломанный .git или репозиторий уровнем выше).
function Test-PinnedClone([string]$Dir, [string]$Url) {
    if (-not (Test-Path -LiteralPath (Join-Path $Dir '.git'))) { return $false }
    $top = Invoke-Native $script:Git @('-C', $Dir, 'rev-parse', '--show-toplevel') -AllowFail -Quiet
    if ($top.ExitCode -ne 0) { return $false }
    $topPath = ([string]($top.Output | Select-Object -First 1)).Trim().Replace('/', '\')
    if ((Get-FullPath $topPath) -ne (Get-FullPath $Dir)) { return $false }
    $origin = Invoke-Native $script:Git @('-C', $Dir, 'remote', 'get-url', 'origin') -AllowFail -Quiet
    if ($origin.ExitCode -ne 0) { return $false }
    $originUrl = ([string]($origin.Output | Select-Object -First 1)).Trim()
    return (($originUrl -replace '\.git$', '').TrimEnd('/') -eq $Url)
}

function Test-Marker([string]$Dir, [string]$Commit, [string]$Artifact) {
    $marker = Join-Path $Dir '.mcp-installed-commit'
    if (-not (Test-Path -LiteralPath $marker) -or -not (Test-Path -LiteralPath $Artifact)) { return $false }
    return ((Get-Content -LiteralPath $marker -Raw).Trim() -eq $Commit)
}

function Set-Marker([string]$Dir, [string]$Commit) {
    Set-Content -LiteralPath (Join-Path $Dir '.mcp-installed-commit') -Value $Commit -Encoding ASCII
}

# Сборка Node-сервера: npm ci по lock-файлу без install-скриптов, нативные модули отдельно.
function Install-NodeServer {
    param(
        [string]$Name,
        [hashtable]$Pin,
        [string]$Dir,
        [string[]]$NativeModules = @(),
        [string]$Entry = 'dist\index.js',
        # Папки сборки, которые лежат в репозитории готовыми: удаляем и собираем из исходников сами.
        [string[]]$CleanPaths = @()
    )
    Get-PinnedRepo $Pin.Url $Pin.Commit $Dir
    $entryPath = Join-Path $Dir $Entry
    # Если сборка лежит в репозитории, git checkout возвращает её готовой: тогда собираем каждый раз.
    if ($CleanPaths.Count -eq 0 -and (Test-Marker $Dir $Pin.Commit $entryPath)) {
        Write-Ok "$Name уже собран, пропускаю сборку"
        return $entryPath
    }
    foreach ($rel in $CleanPaths) {
        $path = Join-Path $Dir $rel
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    $null = Invoke-Native $script:Npm @('ci', '--ignore-scripts', '--no-audit', '--no-fund', '--loglevel=error') -WorkingDirectory $Dir
    foreach ($module in $NativeModules) {
        $null = Invoke-Native $script:Npm @('rebuild', $module, '--loglevel=error') -WorkingDirectory $Dir
    }
    $null = Invoke-Native $script:Npm @('run', 'build', '--silent') -WorkingDirectory $Dir
    $null = Invoke-Native $script:Npm @('prune', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund', '--loglevel=error') -WorkingDirectory $Dir
    if (-not (Test-Path -LiteralPath $entryPath)) { throw "После сборки не найден $entryPath" }
    Set-Marker $Dir $Pin.Commit
    Write-Ok "$Name собран"
    return $entryPath
}

# Копирует папки skills в %USERPROFILE%\.claude\skills. Чужие папки с тем же именем не трогает.
function Install-SkillDirs([string]$SourceRoot, [string[]]$Names, [string]$Commit) {
    $skillsRoot = Join-Path $env:USERPROFILE '.claude\skills'
    New-Item -ItemType Directory -Path $skillsRoot -Force | Out-Null
    $dirs = @(Get-ChildItem -LiteralPath $SourceRoot -Directory | Where-Object { -not $Names -or $Names -contains $_.Name })
    $installed = @()
    foreach ($dir in $dirs) {
        if (-not (Test-Path -LiteralPath (Join-Path $dir.FullName 'SKILL.md'))) { continue }
        $dest = Join-Path $skillsRoot $dir.Name
        $marker = Join-Path $dest '.installed-by-reforger-mcp'
        if ((Test-Path -LiteralPath $dest) -and -not (Test-Path -LiteralPath $marker)) {
            Write-Warn "Оставлен существующий skill: $($dir.Name)"
            continue
        }
        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        Copy-Item -LiteralPath $dir.FullName -Destination $dest -Recurse
        Set-Content -LiteralPath $marker -Value $Commit -Encoding ASCII
        $installed += $dir.Name
    }
    Write-Ok ('Установлены: {0}' -f ($installed -join ', '))
    return $installed
}

function Find-SteamApp([string]$Folder) {
    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        $props = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if (-not $props) { continue }
        foreach ($name in @('SteamPath', 'InstallPath')) {
            if ($props.PSObject.Properties[$name] -and $props.$name) { $roots.Add((([string]$props.$name) -replace '/', '\')) }
        }
    }
    $libraries = New-Object System.Collections.Generic.List[string]
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries.Add($root)
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries.Add(($m.Groups[1].Value -replace '\\\\', '\'))
            }
        }
    }
    foreach ($lib in ($libraries | Select-Object -Unique)) {
        $candidate = Join-Path $lib "steamapps\common\$Folder"
        if (Test-Path -LiteralPath $candidate) { return (Get-FullPath $candidate) }
    }
    return $null
}

# Регистрация MCP (scope user). Все определения копятся в $McpDefs. Есть CLI claude - сервер сразу
# добавляется через `claude mcp add`. Нет (только десктоп-приложение) - в конце $McpDefs одним заходом
# пишется в %USERPROFILE%\.claude.json: его читает вкладка Code. С -WithCursor - ещё и в ~/.cursor/mcp.json.
$McpDefs = [ordered]@{}

# -Secret: в env есть токен, аргументы claude mcp add не выводятся ни в консоль, ни в лог.
function Register-McpStdio([string]$Name, [hashtable]$Environment, [string]$Command, [string[]]$CommandArgs, [switch]$Secret) {
    $envClean = [ordered]@{}
    foreach ($key in ($Environment.Keys | Sort-Object)) {
        if ($Environment[$key]) { $envClean[$key] = [string]$Environment[$key] }
    }
    $script:McpDefs[$Name] = [ordered]@{ type = 'stdio'; command = $Command; args = [string[]]@($CommandArgs); env = $envClean }
    if ($script:Claude) {
        $null = Invoke-Native $script:Claude @('mcp', 'remove', $Name, '--scope', 'user') -AllowFail -Quiet
        $argv = @('mcp', 'add', $Name, '--scope', 'user')
        foreach ($key in $envClean.Keys) { $argv += @('-e', ('{0}={1}' -f $key, $envClean[$key])) }
        $argv += '--'
        $argv += $Command
        $argv += $CommandArgs
        $null = Invoke-Native $script:Claude $argv -Quiet -HideArgs:$Secret
        Write-Ok "Claude Code: $Name зарегистрирован (claude mcp add, scope user)"
    }
    else {
        Write-Ok "$Name будет записан в ~/.claude.json"
    }
}

function Read-Secret([string]$Prompt) {
    $secure = Read-Host -AsSecureString $Prompt
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# Отпечаток дерева npm: уникальные "пакет@версия integrity" из package-lock.json.
# Пути не учитываются, поэтому разная раскладка папок у разных версий npm отпечаток не меняет.
$TreeHashScript = @'
'use strict';
const fs = require('fs');
const crypto = require('crypto');
const lock = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const lines = [...new Set(Object.entries(lock.packages || {})
  .filter(([k]) => k)
  .map(([k, v]) => `${k.slice(k.lastIndexOf('node_modules/') + 13)}@${v.version} ${v.integrity || ''}`))]
  .sort();
process.stdout.write(crypto.createHash('sha256').update(lines.join('\n')).digest('hex'));
'@

function Get-NpmTreeHash([string]$LockPath) {
    $hasher = Join-Path $InstallRoot 'bin\npm-tree-hash.js'
    [System.IO.File]::WriteAllText($hasher, $TreeHashScript, (New-Object System.Text.UTF8Encoding($false)))
    return ([string]((Invoke-Native $script:Node @($hasher, $LockPath) -Quiet).Output | Select-Object -First 1)).Trim()
}

function Register-McpHttp([string]$Name, [string]$Url, [hashtable]$Headers) {
    $clean = [ordered]@{}
    foreach ($key in $Headers.Keys) { $clean[$key] = [string]$Headers[$key] }
    $script:McpDefs[$Name] = [ordered]@{ type = 'http'; url = $Url; headers = $clean }
    if ($script:Claude) {
        $null = Invoke-Native $script:Claude @('mcp', 'remove', $Name, '--scope', 'user') -AllowFail -Quiet
        $argv = @('mcp', 'add', $Name, '--scope', 'user', '--transport', 'http', $Url)
        foreach ($key in $Headers.Keys) { $argv += @('--header', ('{0}: {1}' -f $key, $Headers[$key])) }
        # В заголовках секрет: не выводим аргументы ни в консоль, ни в лог.
        $null = Invoke-Native $script:Claude $argv -Quiet -HideArgs
        Write-Ok "Claude Code: $Name зарегистрирован (claude mcp add, scope user)"
    }
    else {
        Write-Ok "$Name будет записан в ~/.claude.json"
    }
}

# Слияние в ~/.claude.json и ~/.cursor/mcp.json делает node: ~/.claude.json большой,
# и Windows PowerShell 5.1 плохо переписывает такой JSON. Оба файла хранят серверы в "mcpServers".
$MergeScript = @'
'use strict';
// Добавляет MCP-серверы (scope user) в ~/.claude.json, не трогая остальное содержимое.
const fs = require('fs');
const [target, payloadPath] = process.argv.slice(2);
const strip = (s) => s.replace(/^\uFEFF/, '');
const servers = JSON.parse(strip(fs.readFileSync(payloadPath, 'utf8')));
let data = {};
if (fs.existsSync(target)) {
  const raw = strip(fs.readFileSync(target, 'utf8'));
  if (raw.trim()) data = JSON.parse(raw);
  fs.copyFileSync(target, `${target}.bak-${Date.now()}`);
}
if (data === null || typeof data !== 'object' || Array.isArray(data)) {
  throw new Error(`${target}: unexpected JSON root`);
}
if (!data.mcpServers || typeof data.mcpServers !== 'object' || Array.isArray(data.mcpServers)) {
  data.mcpServers = {};
}
for (const [name, config] of Object.entries(servers)) {
  data.mcpServers[name] = config;
  console.log(`mcpServers.${name}: written`);
}
const tmp = `${target}.tmp-${process.pid}`;
fs.writeFileSync(tmp, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
fs.renameSync(tmp, target);
'@

function Save-McpConfig([string]$Target, [System.Collections.IDictionary]$Servers) {
    $merger = Join-Path $InstallRoot 'bin\merge-claude-json.js'
    $payloadPath = Join-Path $InstallRoot 'mcp-payload.json'
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Target) -Force | Out-Null
    [System.IO.File]::WriteAllText($merger, $MergeScript, $utf8)
    try {
        [System.IO.File]::WriteAllText($payloadPath, (ConvertTo-Json -InputObject $Servers -Depth 10), $utf8)
        $null = Invoke-Native $script:Node @($merger, $Target, $payloadPath)
    }
    finally {
        # В payload может быть ключ Obsidian.
        Remove-Item -LiteralPath $payloadPath -Force -ErrorAction SilentlyContinue
    }
}

# Хранилище Obsidian: из -ObsidianVault или из списка хранилищ самого Obsidian (%APPDATA%\obsidian\obsidian.json).
function Find-ObsidianVault {
    $list = Join-Path $env:APPDATA 'obsidian\obsidian.json'
    if (-not (Test-Path -LiteralPath $list)) { return $null }
    try { $json = Get-Content -LiteralPath $list -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $null }
    if (-not $json -or -not $json.PSObject.Properties['vaults']) { return $null }
    $vaults = @($json.vaults.PSObject.Properties | ForEach-Object { $_.Value } |
        Where-Object { $_.PSObject.Properties['path'] -and (Test-Path -LiteralPath $_.path) })
    if ($vaults.Count -eq 1) { return [string]$vaults[0].path }
    $open = @($vaults | Where-Object { $_.PSObject.Properties['open'] -and $_.open })
    if ($open.Count -eq 1) { return [string]$open[0].path }
    if ($vaults.Count -gt 1) {
        Write-Warn ('Несколько хранилищ Obsidian, укажите нужное через -ObsidianVault: {0}' -f (($vaults | ForEach-Object { $_.path }) -join '; '))
    }
    return $null
}

# Свой skill: как пользоваться этим набором MCP и где вести заметки в Obsidian.
$WorkflowSkill = @'
---
name: reforger-obsidian-workflow
description: Рабочий процесс моддинга Arma Reforger 1.9 (Enfusion, Workbench, выделенный сервер) с MCP-серверами enfusion-mcp, enfusion-audit, arga-enfusion, blender и ведение заметок о модах и аудитах в Obsidian. Use when creating, compiling, testing, auditing or publishing Reforger mods or a dedicated server, choosing which of these MCP tools to call, or saving mod docs and audit reports to the Obsidian vault.
---

# Reforger + Obsidian: рабочий процесс

## Какой сервер для чего

| Задача | Сервер |
|---|---|
| API и вики, создание мода, скрипты, префабы, компиляция (`mod compile`), сборка, тестовый сервер, Workbench (`wb_*`) | `enfusion-mcp` |
| Аудит: битые GUID, коллизии GUID, цепочки `modded class`, линтер, логи, `server.json` | `enfusion-audit` (только чтение) |
| Чтение `.pak` модов из Workshop без распаковки | `arga-enfusion` |
| 3D-ассеты | `blender` |
| Заметки | `obsidian-vault` (файлы хранилища) или `obsidian` (открытый Obsidian) |

## Правила безопасности

- Workbench управляет только `enfusion-mcp`. Инструменты `wb_*` у `enfusion-audit` запрещены, не пытайся их вызвать.
- Диалог Workbench "Script Authorization Required" решает пользователь. Не предлагай `wb_watch` с `answer: yes` для `Workbench.RunCmd` и `Workbench.RunProcess` из чужих модов. В остальных случаях сначала покажи пользователю скрипт, строку и метод.
- Чужие моды проверяй только по копии в `__AUDIT_WORKSHOP__`, оригиналы не трогай.
- Публикует в Workshop только пользователь, вручную. Перед публикацией: `wb_cleanup`, затем `project_validate` и `workshop_validate_manifest` у `enfusion-audit`.
- Не ставь MCP-серверы через `npx -y <пакет>` без версии и не меняй их версии: их ставит Install-ReforgerMcp.ps1 после аудита кода.
- Не записывай в заметки секреты: пароли RCON и администратора, API-ключи, токены. Пиши "задан" вместо значения.

## Аудит модов сервера

1. Состав и зависимости: `server_mod_list`, `workshop_check_deps`, `list_dependencies`.
2. Целостность: `find_broken_refs`, `refactor_merge_duplicate_guids` (только диагностика), `inheritance_chain`.
3. Конфликты скриптов: `script_overrides`, `script_class_hierarchy`, `script_lint`.
4. Безопасность чужих модов: `pak_grep` по `RplRpc`, `RestApi|RestContext`, `FileIO`, затем `pak_cat` подозрительных мест.
5. Конфиг сервера: `server_validate_config`.
6. Логи: `logs_summarize_errors`, `logs_filter`.
7. Прогон: локальный сервер (`server` у `enfusion-mcp`) с тем же набором модов, подключение клиентом.
8. Живой сервер: только `server_health_probe`.

## Создание мода

1. Изучение: `api_search`, `wiki_search`, `game_read`.
2. Каркас и код в `enfusion-mcp`.
3. Цикл `mod compile` до чистых ошибок `file:line`.
4. Проверка перед релизом: `project_validate`, `script_lint`, `find_broken_refs`, `workshop_validate_manifest`.
5. Тест на локальном сервере.
6. Публикация вручную пользователем.

## Заметки в Obsidian

Пиши синтаксисом Obsidian (skill `obsidian-markdown`): `[[ссылки]]`, callouts `> [!warning]`. Записывай через `write_note` и `patch_note` у `obsidian-vault`. Удаление заметок запрещено настройками: если заметка не нужна, предложи пользователю удалить её самому.

Структура:

- `Reforger/Mods/<Имя мода>.md` - карточка мода.
- `Reforger/Audits/<ГГГГ-ММ-ДД> <сервер>.md` - отчёт аудита.
- `Reforger/Server/<сервер>.md` - настройки сервера без секретов.

Карточка мода:

```markdown
---
type: reforger-mod
guid:
version:
game_version: "1.9"
status: draft
dependencies: []
tags: [reforger, mod]
---
## Назначение
## Зависимости
Ссылки [[...]] на карточки модов.
## Изменённые классы
Список `modded class` и чем они рискуют конфликтовать.
## Проверки
| Дата | Что | Результат |
## Известные проблемы
```

Отчёт аудита:

```markdown
---
type: reforger-audit
date:
server:
mods_checked: 0
critical: 0
warnings: 0
tags: [reforger, audit]
---
## Итог
> [!warning] Критичное
## Зависимости
## Целостность ресурсов
## Конфликты скриптов
## Безопасность чужих модов
## Конфиг и логи сервера
## Что сделать
```

Факты отделяй от предположений: у каждой находки укажи инструмент и файл, из которых она получена.
'@

# ---------------------------------------------------------------------------
# 0. Окружение
# ---------------------------------------------------------------------------
if ($env:OS -ne 'Windows_NT') { throw 'Скрипт рассчитан на Windows 10/11.' }

# Вывод node/git/uv в UTF-8. В хостах без консоли (ISE) свойство недоступно - пропускаем.
$prevOutputEncoding = $null
try { $prevOutputEncoding = [Console]::OutputEncoding; [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
# Windows PowerShell 5.1 иначе сериализует массивы в JSON как {"value": [...], "Count": N}.
if ($PSVersionTable.PSVersion.Major -lt 6) { Remove-TypeData -TypeName System.Array -ErrorAction SilentlyContinue }

$InstallRoot = Get-FullPath $InstallRoot
if (-not $AuditRoot) { $AuditRoot = Join-Path $InstallRoot 'audit' }
$AuditRoot = Get-FullPath $AuditRoot
New-Item -ItemType Directory -Path $InstallRoot, $AuditRoot, (Join-Path $InstallRoot 'bin') -Force | Out-Null

$logPath = Join-Path $InstallRoot ('install-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))
Start-Transcript -Path $logPath | Out-Null

try {
    Write-Step 'Проверка окружения'
    $identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warn 'Скрипт запущен от администратора. Лучше запускать от обычного пользователя: иначе git позже ругается на владельца папок (dubious ownership).'
    }
    if ($InstallRoot -match '[^\x20-\x7E]') {
        Write-Warn "В пути $InstallRoot есть не-ASCII символы. Сборка cargo/node-gyp может сломаться, лучше C:\mcp."
    }

    if ($InstallPrereqs) {
        $winget = Resolve-Exe 'winget'
        if (-not $winget) { throw 'winget не найден. Установите App Installer из Microsoft Store или поставьте git, Node.js LTS и uv вручную.' }
        $packages = [ordered]@{ git = 'Git.Git'; node = 'OpenJS.NodeJS.LTS'; uv = 'astral-sh.uv' }
        foreach ($tool in $packages.Keys) {
            if (-not (Resolve-Exe $tool)) {
                Write-Host "    winget: $($packages[$tool])"
                $null = Invoke-Native $winget @('install', '--id', $packages[$tool], '-e', '--source', 'winget', '--silent', '--accept-package-agreements', '--accept-source-agreements')
            }
        }
        Update-SessionPath
    }

    $script:Git = Resolve-Exe 'git'
    $script:Node = Resolve-Exe 'node'
    $script:Npm = Resolve-Exe 'npm'
    $script:Uv = Resolve-Exe 'uv'
    $script:Claude = Resolve-Exe 'claude'
    $missing = @()
    if (-not $script:Git) { $missing += 'git (winget install Git.Git)' }
    if (-not $script:Node -or -not $script:Npm) { $missing += 'Node.js 22 LTS (winget install OpenJS.NodeJS.LTS)' }
    if (-not $script:Uv -and (-not $SkipArga -or -not $SkipBlender)) { $missing += 'uv (winget install astral-sh.uv)' }
    if ($missing.Count -gt 0) {
        throw ("Не найдено: {0}. Поставьте вручную или запустите скрипт с -InstallPrereqs." -f ($missing -join '; '))
    }
    $nodeVersion = ([string]((Invoke-Native $script:Node @('--version') -Quiet).Output | Select-Object -First 1)).Trim()
    $nodeSemver = [version](($nodeVersion -replace '^v', '') -replace '[^0-9.].*$', '')
    $nodeMajor = $nodeSemver.Major
    if ($nodeMajor -lt 20) { throw "Нужен Node.js 20+, найден $nodeVersion." }
    if ($nodeMajor -gt 26) { Write-Warn "Node $nodeVersion новее, чем поддерживает better-sqlite3 12.10 (20-26). Рекомендую Node 22 LTS." }
    Write-Ok "git, node $nodeVersion, npm найдены"
    if ($script:Claude) {
        Write-Ok "claude CLI: $($script:Claude) - регистрация через claude mcp add"
    }
    else {
        Write-Warn 'claude CLI нет в PATH (нормально, если стоит только десктоп-приложение). Серверы будут записаны в %USERPROFILE%\.claude.json в конце работы скрипта.'
        if (@(Get-Process -Name 'claude' -ErrorAction SilentlyContinue).Count -gt 0) {
            Write-Warn 'Claude Desktop сейчас запущен. Закройте его полностью (значок в трее > Quit) до конца сборки, иначе запись конфигурации будет отменена.'
        }
    }

    # Пути игры
    if (-not $ToolsPath) { $ToolsPath = Find-SteamApp 'Arma Reforger Tools' }
    if (-not $GamePath) { $GamePath = Find-SteamApp 'Arma Reforger' }
    if ($ToolsPath) { $ToolsPath = Get-FullPath $ToolsPath; Write-Ok "Arma Reforger Tools: $ToolsPath" }
    else { Write-Warn 'Arma Reforger Tools не найдены (Steam). Компиляция и Workbench работать не будут. Укажите -ToolsPath.' }
    if ($GamePath) { $GamePath = Get-FullPath $GamePath; Write-Ok "Arma Reforger: $GamePath" }
    else { Write-Warn 'Arma Reforger не найдена (Steam). Укажите -GamePath.' }

    $documents = [Environment]::GetFolderPath('MyDocuments')
    $wbAddons = Join-Path $documents 'My Games\ArmaReforgerWorkbench\addons'
    $wbPacked = Join-Path $documents 'My Games\ArmaReforgerWorkbench\packed'
    $wbLogs = Join-Path $documents 'My Games\ArmaReforgerWorkbench\logs'
    if (-not $WorkshopSource) { $WorkshopSource = Join-Path $documents 'My Games\ArmaReforger\addons' }
    $auditWorkshop = Join-Path $AuditRoot 'workshop'
    New-Item -ItemType Directory -Path $wbAddons, $auditWorkshop -Force | Out-Null
    if (-not $AssetsRoot) { $AssetsRoot = Join-Path $documents 'ReforgerAssets' }
    $AssetsRoot = Get-FullPath $AssetsRoot

    $env:DISABLE_TELEMETRY = 'true'
    # Python в русской Windows пишет в консоль в cp1251 и падает на символах вроде стрелки.
    # Режим UTF-8 для всех Python-программ, которые запускает этот скрипт.
    $env:PYTHONUTF8 = '1'

    # -----------------------------------------------------------------------
    # 1. enfusion-mcp (создание модов)
    # -----------------------------------------------------------------------
    Write-Step 'enfusion-mcp-SyykoNT (создание под Reforger 1.9, компиляция, тестовый сервер)'
    try {
        $creatorEntry = Install-NodeServer 'enfusion-mcp-syykont' $Pins.Creator (Join-Path $InstallRoot 'enfusion-mcp-syykont') @()
        Register-McpStdio 'enfusion-mcp' @{
            ENFUSION_WORKBENCH_PATH = $ToolsPath
            ENFUSION_GAME_PATH      = $GamePath
            ENFUSION_PROJECT_PATH   = $wbAddons
            ENFUSION_PACKED_PATH    = $wbPacked
            # Тестовый выделенный сервер и SteamCMD - рядом с остальным (-InstallRoot), а не в профиле на C:.
            ENFUSION_SERVER_PATH    = (Join-Path $InstallRoot 'reforger-server')
            ENFUSION_STEAMCMD_PATH  = (Join-Path $InstallRoot 'steamcmd')
            ENFUSION_SERVER_PROFILE = (Join-Path $InstallRoot 'reforger-server-profile')
            # Сторож Workbench по умолчанию сам жмёт "Yes" в диалоге Script Authorization Required
            # для скриптов из всех загруженных аддонов, включая чужие зависимости. Отключаем: решает человек.
            ENFUSION_SCRIPT_AUTH    = 'off'
        } $script:Node @($creatorEntry)
        Add-Result 'enfusion-mcp' 'OK' 'SyykoNT v0.16.0 (Reforger 1.9)'
        $oldMa = Join-Path $InstallRoot 'enfusion-mcp-ma'
        if (Test-Path -LiteralPath $oldMa) {
            Write-Warn "Прежняя версия больше не используется, папку можно удалить: $oldMa"
        }
    }
    catch { Write-Warn $_.Exception.Message; Add-Result 'enfusion-mcp' 'FAILED' $_.Exception.Message }

    # -----------------------------------------------------------------------
    # 2. enfusion-audit (аудит)
    # -----------------------------------------------------------------------
    Write-Step 'enfusion-workbench-mcp (аудит)'
    try {
        $auditEntry = Install-NodeServer 'enfusion-audit' $Pins.Audit (Join-Path $InstallRoot 'enfusion-audit') @('better-sqlite3')
        Register-McpStdio 'enfusion-audit' @{
            ENFUSION_WORKBENCH_PATH     = $ToolsPath
            ENFUSION_GAME_PATH          = $GamePath
            ENFUSION_PROJECT_PATH       = $wbAddons
            ENFUSION_WORKSHOP_PATH      = $auditWorkshop
            ENFUSION_PROJECT_INDEX_PATH = (Join-Path $AuditRoot 'project-index.db')
            ENFUSION_LOGS_PATH          = $wbLogs
            ENFUSION_GAME_LOGS_PATH     = $ServerLogsPath
            # Нерабочий порт: даже при снятом запрете аудит не подключится к Workbench enfusion-mcp.
            ENFUSION_WORKBENCH_PORT     = '5799'
        } $script:Node @($auditEntry)
        Add-Result 'enfusion-audit' 'OK' 'Workbench и запись запрещены'
    }
    catch { Write-Warn $_.Exception.Message; Add-Result 'enfusion-audit' 'FAILED' $_.Exception.Message }

    # -----------------------------------------------------------------------
    # 3. arga-enfusion (.pak модов) + enfusion CLI из исходников
    # -----------------------------------------------------------------------
    $cargo = Resolve-Exe 'cargo'
    if ($SkipArga) { Add-Result 'arga-enfusion' 'SKIPPED' '-SkipArga' }
    elseif (-not $cargo) {
        # В релизах enfusion_tools есть только UI, CLI собирается из исходников.
        Write-Warn 'arga-enfusion пропущен: нет cargo. Поставьте winget install Rustlang.Rustup и Visual Studio Build Tools (C++), затем перезапустите скрипт.'
        Add-Result 'arga-enfusion' 'SKIPPED' 'нет Rust (cargo)'
    }
    else {
        Write-Step 'arga_enfusion_mcp + enfusion CLI'
        try {
            if (-not $GamePath) { throw 'Не найден путь к игре (-GamePath): нечего читать.' }
            $toolsDir = Join-Path $InstallRoot 'enfusion-tools'
            $cli = Join-Path $InstallRoot 'bin\enfusion.exe'
            Get-PinnedRepo $Pins.Tools.Url $Pins.Tools.Commit $toolsDir
            if (-not (Test-Marker $toolsDir $Pins.Tools.Commit $cli)) {
                $build = Invoke-Native $cargo @('build', '--release', '--locked', '-p', 'enfusion_cli') -WorkingDirectory $toolsDir -AllowFail
                if ($build.ExitCode -ne 0) {
                    throw 'cargo build завершился с ошибкой. Частая причина: нет Visual Studio Build Tools с компонентом C++ (link.exe).'
                }
                Copy-Item -LiteralPath (Join-Path $toolsDir 'target\release\enfusion.exe') -Destination $cli -Force
                Set-Marker $toolsDir $Pins.Tools.Commit
            }
            Write-Ok "enfusion CLI: $cli"

            $argaDir = Join-Path $InstallRoot 'arga-enfusion'
            Get-PinnedRepo $Pins.Arga.Url $Pins.Arga.Commit $argaDir
            $null = Invoke-Native $script:Uv @('sync', '--frozen', '--no-dev') -WorkingDirectory $argaDir
            # REFORGER_EXTRACT_DIR намеренно не задан: извлечение выключено (в этой версии CLI его и нет).
            Register-McpStdio 'arga-enfusion' @{
                ENFUSION_CLI      = $cli
                REFORGER_DATA_DIR = (Join-Path $GamePath 'addons\data')
                REFORGER_MODS_DIR = $auditWorkshop
                PYTHONUTF8        = '1'
            } $script:Uv @('--directory', $argaDir, 'run', '--frozen', '--no-dev', 'arga-enfusion-mcp')
            Add-Result 'arga-enfusion' 'OK' 'только чтение'
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'arga-enfusion' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 4. Blender MCP
    # -----------------------------------------------------------------------
    if ($SkipBlender) { Add-Result 'blender' 'SKIPPED' '-SkipBlender' }
    else {
        Write-Step 'Blender MCP'
        try {
            $blenderDir = Join-Path $InstallRoot 'blender-mcp'
            Get-PinnedRepo $Pins.Blender.Url $Pins.Blender.Commit $blenderDir
            $null = Invoke-Native $script:Uv @('lock', '--upgrade', '--exclude-newer', $BlenderExcludeNewer) -WorkingDirectory $blenderDir
            $null = Invoke-Native $script:Uv @('sync', '--frozen', '--no-dev') -WorkingDirectory $blenderDir

            $export = (Invoke-Native $script:Uv @('export', '--frozen', '--no-dev', '--no-emit-project', '--no-hashes', '--format', 'requirements-txt') -WorkingDirectory $blenderDir -Quiet).Output
            $mcpLine = $export | Where-Object { $_ -match '^mcp==' } | Select-Object -First 1
            if ($mcpLine -and ([version](($mcpLine -replace '^mcp==', '') -replace '[^0-9.].*$', '')) -lt $BlenderMinMcp) {
                Write-Warn "В lock-файле осталась уязвимая версия: $mcpLine"
            }

            $addon = Invoke-Native $script:Uv @('run', '--frozen', '--no-dev', 'mcp-for-blender', 'install-addon') -WorkingDirectory $blenderDir -AllowFail
            $addonNote = 'аддон скопирован'
            if ($addon.ExitCode -ne 0) {
                $addonNote = 'АДДОН НЕ СКОПИРОВАН'
                Write-Warn "Аддон не скопирован: запустите Blender один раз и перезапустите скрипт, либо установите вручную $blenderDir\addon.py (Edit > Preferences > Add-ons > Install from Disk)."
            }
            # Аддон при старте Blender скачивает addon.py из ветки main и предлагает кнопкой заменить себя
            # непроверенной версией. Переменная окружения отключает эту проверку.
            [Environment]::SetEnvironmentVariable('BLENDERMCP_NO_UPDATE_CHECK', '1', 'User')
            $env:BLENDERMCP_NO_UPDATE_CHECK = '1'

            Register-McpStdio 'blender' @{
                DISABLE_TELEMETRY     = 'true'
                BLENDER_MCP_SAFE_MODE = '1'
                PYTHONUTF8            = '1'
            } $script:Uv @('--directory', $blenderDir, 'run', '--frozen', '--no-dev', 'mcp-for-blender')
            Add-Result 'blender' 'OK' "телеметрия выкл., safe mode вкл., автообновление аддона выкл.; $addonNote"
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'blender' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 5. Skills для Arma Reforger
    # -----------------------------------------------------------------------
    if ($SkipSkill) { Add-Result 'reforger-skills' 'SKIPPED' '-SkipSkill' }
    else {
        Write-Step 'Skills для Arma Reforger'
        # Каждый источник ставится отдельно: сбой одного не отменяет остальные.
        $skillsRoot = Join-Path $env:USERPROFILE '.claude\skills'
        $done = @()
        $kept = @()
        $failed = @()

        try {
            $skillDir = Join-Path $skillsRoot 'arma-reforger-modding'
            # Своя папка с тем же именем (например, собственный SKILL.md) не перезаписывается.
            if ((Test-Path -LiteralPath $skillDir) -and -not (Test-PinnedClone $skillDir $Pins.Skill.Url)) {
                Write-Warn "В $skillDir уже лежит свой skill (не клон Mavericktfius) - оставлен как есть."
                $kept += 'arma-reforger-modding (свой)'
            }
            else {
                Get-PinnedRepo $Pins.Skill.Url $Pins.Skill.Commit $skillDir
                $done += 'arma-reforger-modding'
            }
        }
        catch { Write-Warn $_.Exception.Message; $failed += "arma-reforger-modding: $($_.Exception.Message)" }

        try {
            # Reidond/stavka - большой репозиторий, из него берём только три skill-а (только Markdown, проверено).
            $reidondRepo = Join-Path $InstallRoot 'reidond-stavka'
            Get-PinnedRepo $Pins.ReidondSkills.Url $Pins.ReidondSkills.Commit $reidondRepo
            $done += @(Install-SkillDirs (Join-Path $reidondRepo '.agents\skills') @('arma-reforger', 'arma-reforger-server-config', 'arma-reforger-server-hosting') $Pins.ReidondSkills.Commit)
        }
        catch { Write-Warn $_.Exception.Message; $failed += "Reidond: $($_.Exception.Message)" }

        try {
            $ownDir = Join-Path $skillsRoot 'reforger-obsidian-workflow'
            $ownMarker = Join-Path $ownDir '.installed-by-reforger-mcp'
            if ((Test-Path -LiteralPath $ownDir) -and -not (Test-Path -LiteralPath $ownMarker)) {
                Write-Warn 'Оставлен существующий skill: reforger-obsidian-workflow'
                $kept += 'reforger-obsidian-workflow (свой)'
            }
            else {
                New-Item -ItemType Directory -Path $ownDir -Force | Out-Null
                # Без BOM: иначе frontmatter SKILL.md не распознаётся.
                $text = $WorkflowSkill.Replace('__AUDIT_WORKSHOP__', $auditWorkshop)
                [System.IO.File]::WriteAllText((Join-Path $ownDir 'SKILL.md'), $text, (New-Object System.Text.UTF8Encoding($false)))
                Set-Content -LiteralPath $ownMarker -Value 'Install-ReforgerMcp.ps1' -Encoding ASCII
                $done += 'reforger-obsidian-workflow'
            }
        }
        catch { Write-Warn $_.Exception.Message; $failed += "workflow: $($_.Exception.Message)" }

        $note = 'установлены: {0}' -f ($done -join ', ')
        if ($kept.Count -gt 0) { $note += '; оставлены: {0}' -f ($kept -join ', ') }
        if ($failed.Count -gt 0) { Add-Result 'reforger-skills' 'FAILED' ('{0}; ошибки: {1}' -f $note, ($failed -join ' | ')) }
        else { Add-Result 'reforger-skills' 'OK' $note }
    }

    # -----------------------------------------------------------------------
    # 5a. Obsidian: официальные skills и (по желанию) MCP плагина Local REST API
    # -----------------------------------------------------------------------
    if ($SkipObsidianSkills) { Add-Result 'obsidian-skills' 'SKIPPED' '-SkipObsidianSkills' }
    else {
        Write-Step 'Obsidian skills (obsidianmd/obsidian-skills)'
        try {
            $obsRepo = Join-Path $InstallRoot 'obsidian-skills'
            Get-PinnedRepo $Pins.ObsidianSkills.Url $Pins.ObsidianSkills.Commit $obsRepo
            $installed = @(Install-SkillDirs (Join-Path $obsRepo 'plugins\obsidian\skills') @() $Pins.ObsidianSkills.Commit)
            Add-Result 'obsidian-skills' 'OK' ('{0} skills' -f $installed.Count)
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'obsidian-skills' 'FAILED' $_.Exception.Message }
    }

    # MCPVault: работает с файлами хранилища напрямую, Obsidian и плагины не нужны.
    if ($SkipObsidianVault) { Add-Result 'obsidian-vault' 'SKIPPED' '-SkipObsidianVault' }
    else {
        Write-Step 'Obsidian MCP по файлам хранилища (bitbonsai/mcpvault)'
        try {
            if ($nodeSemver -lt $VaultMinNode) { throw "mcpvault нужен Node.js $VaultMinNode+, найден $nodeVersion." }
            $vault = if ($ObsidianVault) { $ObsidianVault } else { Find-ObsidianVault }
            if (-not $vault) { throw 'Хранилище Obsidian не найдено. Укажите путь: -ObsidianVault "D:\Notes\MyVault".' }
            if (-not (Test-Path -LiteralPath $vault -PathType Container)) { throw "Нет папки хранилища: $vault" }
            $vault = Get-FullPath $vault
            Write-Ok "Хранилище: $vault"
            # dist лежит в репозитории готовым: удаляем и собираем из проверенных исходников.
            $vaultEntry = Install-NodeServer -Name 'mcpvault' -Pin $Pins.Vault -Dir (Join-Path $InstallRoot 'mcpvault') -Entry 'dist\server.js' -CleanPaths @('dist')
            $vaultArgs = @($vaultEntry, $vault)
            if ($ObsidianReadOnly) { $vaultArgs += '--read-only' }
            Register-McpStdio 'obsidian-vault' @{} $script:Node $vaultArgs
            $mode = if ($ObsidianReadOnly) { 'только чтение' } else { 'чтение и запись, удаление запрещено' }
            Add-Result 'obsidian-vault' 'OK' "$mode; $vault"
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'obsidian-vault' 'FAILED' $_.Exception.Message }
    }

    if (-not $WithObsidian) { Add-Result 'obsidian-mcp' 'SKIPPED' 'нужен -WithObsidian' }
    else {
        $obsidianUrl = 'http://127.0.0.1:{0}/mcp/' -f $ObsidianPort
        Write-Step "Obsidian MCP: $obsidianUrl"
        try {
            # Ключ читается скрыто: не попадает ни в историю PowerShell, ни в лог.
            $secure = Read-Host -AsSecureString 'API-ключ из Obsidian (Settings > Local REST API), ввод скрыт'
            $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
            try { $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
            finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
            if (-not $apiKey) { throw 'Ключ не введён.' }
            Register-McpHttp 'obsidian' $obsidianUrl @{ Authorization = "Bearer $apiKey" }
            $apiKey = $null
            Add-Result 'obsidian-mcp' 'OK' $obsidianUrl
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'obsidian-mcp' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 5b. Substance 3D Painter MCP (текстуры)
    # -----------------------------------------------------------------------
    if ($SkipPainter) { Add-Result 'substance-painter' 'SKIPPED' '-SkipPainter' }
    elseif (-not $script:Uv) { Add-Result 'substance-painter' 'SKIPPED' 'нет uv' }
    else {
        Write-Step 'Substance 3D Painter MCP (elliezu/SubstancePainterMCP)'
        try {
            $painterDir = Join-Path $InstallRoot 'substance-painter-mcp'
            Get-PinnedRepo $Pins.Painter.Url $Pins.Painter.Commit $painterDir
            $null = Invoke-Native $script:Uv @('lock', '--exclude-newer', $PainterExcludeNewer) -WorkingDirectory $painterDir
            $null = Invoke-Native $script:Uv @('sync', '--frozen', '--no-dev') -WorkingDirectory $painterDir
            $painterFolders = [ordered]@{}
            foreach ($sub in @('export', 'painter', 'meshes', 'resources')) {
                $painterFolders[$sub] = Join-Path $AssetsRoot $sub
                New-Item -ItemType Directory -Path $painterFolders[$sub] -Force | Out-Null
            }
            # Сервер пишет и читает только внутри этих папок. Экспорт текстур - ещё и прямо в моды Workbench.
            # SP_MCP_ALLOW_EXECUTE_PYTHON намеренно не задан: произвольный Python в Painter выключен.
            Register-McpStdio 'substance-painter' @{
                SP_MCP_EXPORT_ROOTS    = (@($painterFolders['export'], $wbAddons) -join ';')
                SP_MCP_PROJECT_ROOTS   = $painterFolders['painter']
                SP_MCP_MESH_ROOTS      = $painterFolders['meshes']
                SP_MCP_BAKE_MESH_ROOTS = $painterFolders['meshes']
                SP_MCP_RESOURCE_ROOTS  = $painterFolders['resources']
                PYTHONUTF8             = '1'
            } $script:Uv @('--directory', $painterDir, 'run', '--frozen', '--no-dev', 'substance-painter-mcp')
            Add-Result 'substance-painter' 'OK' "папки: $AssetsRoot"
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'substance-painter' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 5c. Discord: skill для разработки ботов и MCP для своего сервера
    # -----------------------------------------------------------------------
    if ($SkipDiscordSkill) { Add-Result 'discord-skill' 'SKIPPED' '-SkipDiscordSkill' }
    else {
        Write-Step 'Skill discord-bot-stinger'
        try {
            # Из большого репозитория берётся только папка skill-а (частичное клонирование).
            $stingerRepo = Join-Path $InstallRoot 'discord-bot-stinger'
            Get-PinnedRepo $Pins.DiscordSkill.Url $Pins.DiscordSkill.Commit $stingerRepo @('/plugins/platform-and-release-tools/skills/discord-bot-stinger/', '/LICENSE.md')
            $installed = @(Install-SkillDirs (Join-Path $stingerRepo 'plugins\platform-and-release-tools\skills') @('discord-bot-stinger') $Pins.DiscordSkill.Commit)
            Add-Result 'discord-skill' 'OK' ('{0} (AGPL-3.0)' -f ($installed -join ', '))
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'discord-skill' 'FAILED' $_.Exception.Message }
    }

    if (-not $WithDiscord) { Add-Result 'discord-mcp' 'SKIPPED' 'нужен -WithDiscord' }
    else {
        Write-Step "Discord MCP ($DiscordPackage $DiscordVersion)"
        try {
            if ($nodeSemver -lt $DiscordMinNode) { throw "Discord MCP нужен Node.js $DiscordMinNode+, найден $nodeVersion." }
            $guild = $DiscordGuildId
            if (-not $guild) {
                $guild = Read-Host 'ID вашего Discord-сервера (Discord: Настройки > Расширенные > Режим разработчика, затем ПКМ по серверу > Копировать ID)'
            }
            $guild = ([string]$guild).Trim()
            if ($guild -notmatch '^\d{17,20}$') { throw "ID сервера - это 17-20 цифр, получено: '$guild'." }

            $discordDir = Join-Path $InstallRoot 'discord-mcp'
            New-Item -ItemType Directory -Path $discordDir -Force | Out-Null
            $manifest = '{{ "name": "discord-mcp-install", "private": true, "dependencies": {{ "{0}": "{1}" }} }}' -f $DiscordPackage, $DiscordVersion
            [System.IO.File]::WriteAllText((Join-Path $discordDir 'package.json'), $manifest, (New-Object System.Text.UTF8Encoding($false)))
            $lockPath = Join-Path $discordDir 'package-lock.json'
            $discordEntry = Join-Path $discordDir 'node_modules\@discord-mcp\cli\dist\cli.js'
            $ready = (Test-Path -LiteralPath $discordEntry) -and (Test-Path -LiteralPath $lockPath) -and ((Get-NpmTreeHash $lockPath) -eq $DiscordTreeHash)
            if (-not $ready) {
                # --before: только версии, опубликованные до даты проверки. Install-скрипты пакетов выключены.
                $null = Invoke-Native $script:Npm @('install', '--ignore-scripts', '--no-audit', '--no-fund', '--omit=dev', "--before=$DiscordBefore", '--loglevel=error') -WorkingDirectory $discordDir
            }
            $treeHash = Get-NpmTreeHash $lockPath
            if ($treeHash -ne $DiscordTreeHash) {
                throw "Дерево зависимостей отличается от проверенного ($treeHash). Сервер не зарегистрирован."
            }
            if (-not (Test-Path -LiteralPath $discordEntry)) { throw "Не найден $discordEntry" }
            Write-Ok 'Дерево зависимостей совпадает с проверенным'

            $token = Read-Secret 'Токен бота (Developer Portal > Bot > Reset Token), ввод скрыт'
            if (-not $token) { throw 'Токен не введён.' }
            # Изменения: preview блокирует все записи; удаление и бан - только пробный прогон,
            # пока в конфиге вручную не задан MCP_DRY_RUN=false.
            $writeMode = if ($DiscordAllowWrites) { 'allow' } else { 'preview' }
            Register-McpStdio 'discord' @{
                DISCORD_TOKEN            = $token
                ALLOWED_GUILDS           = $guild
                DISCORD_DEFAULT_GUILD_ID = $guild
                MCP_WRITE_MODE           = $writeMode
                MCP_ACCESS_MODE          = 'enforce'
                MCP_DM_CONSENT_MODE      = 'require'
                MCP_CATEGORIES           = ($DiscordCategories -join ',')
            } $script:Node @($discordEntry, 'serve') -Secret
            $token = $null
            Add-Result 'discord-mcp' 'OK' "сервер $guild; запись: $writeMode; удаление: пробный прогон"
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'discord-mcp' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 6. Копия Workshop-модов для аудита
    # -----------------------------------------------------------------------
    if ($SkipWorkshopCopy) { Add-Result 'workshop-copy' 'SKIPPED' '-SkipWorkshopCopy' }
    else {
        Write-Step "Копия модов: $WorkshopSource -> $auditWorkshop"
        try {
            if (-not (Test-Path -LiteralPath $WorkshopSource)) {
                throw "Нет папки $WorkshopSource. Зайдите клиентом на свой сервер (игра скачает его моды) или укажите -WorkshopSource."
            }
            $source = Get-FullPath $WorkshopSource
            if ($source -eq $auditWorkshop) { throw 'Источник и копия совпадают.' }
            # /MIR удаляет лишнее в приёмнике, поэтому работаем только с папкой, помеченной этим скриптом.
            $marker = Join-Path $auditWorkshop '.reforger-audit-copy'
            $hasContent = @(Get-ChildItem -LiteralPath $auditWorkshop -Force | Where-Object { $_.Name -ne '.reforger-audit-copy' }).Count -gt 0
            if ($hasContent -and -not (Test-Path -LiteralPath $marker)) {
                throw "В $auditWorkshop уже есть файлы, не созданные этим скриптом. Очистите папку или укажите другой -AuditRoot."
            }
            Set-Content -LiteralPath $marker -Value 'managed by Install-ReforgerMcp.ps1' -Encoding ASCII
            $robocopy = Join-Path $env:SystemRoot 'System32\robocopy.exe'
            $copy = Invoke-Native $robocopy @($source, $auditWorkshop, '/MIR', '/XF', '.reforger-audit-copy', '/R:1', '/W:1', '/NFL', '/NDL', '/NP', '/NJH') -AllowFail
            Set-Content -LiteralPath $marker -Value 'managed by Install-ReforgerMcp.ps1' -Encoding ASCII
            if ($copy.ExitCode -ge 8) { throw "robocopy завершился с кодом $($copy.ExitCode)" }
            $mods = @(Get-ChildItem -LiteralPath $auditWorkshop -Directory).Count
            Add-Result 'workshop-copy' 'OK' "$mods модов в $auditWorkshop"
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'workshop-copy' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 7. Запреты для enfusion-audit в настройках Claude Code
    # -----------------------------------------------------------------------
    if ($SkipPermissions) { Add-Result 'permissions' 'SKIPPED' '-SkipPermissions' }
    else {
        Write-Step 'Запреты для enfusion-audit и Obsidian (~/.claude/settings.json)'
        try {
            $claudeDir = Join-Path $env:USERPROFILE '.claude'
            $settingsPath = Join-Path $claudeDir 'settings.json'
            New-Item -ItemType Directory -Path $claudeDir -Force | Out-Null
            $settings = $null
            if (Test-Path -LiteralPath $settingsPath) {
                $raw = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8
                if ($raw -and $raw.Trim()) {
                    try { $settings = $raw | ConvertFrom-Json }
                    catch { throw "settings.json содержит невалидный JSON, не трогаю его: $settingsPath" }
                }
                Copy-Item -LiteralPath $settingsPath -Destination ('{0}.bak-{1:yyyyMMdd-HHmmss}' -f $settingsPath, (Get-Date))
            }
            if (-not $settings) { $settings = New-Object psobject }
            if (-not $settings.PSObject.Properties['permissions']) {
                $settings | Add-Member -NotePropertyName 'permissions' -NotePropertyValue (New-Object psobject)
            }
            $permissions = $settings.permissions
            $deny = [string[]]@(@($AuditDenyTools | ForEach-Object { "mcp__enfusion-audit__$_" }) + $ObsidianDenyRules)
            if ($permissions.PSObject.Properties['deny']) {
                $permissions.deny = [string[]]@(@($permissions.deny) + $deny | Select-Object -Unique)
            }
            else {
                $permissions | Add-Member -NotePropertyName 'deny' -NotePropertyValue $deny
            }
            # Windows PowerShell 5.1 иначе сериализует массивы как {"value": [...], "Count": N}.
            if ($PSVersionTable.PSVersion.Major -lt 6) { Remove-TypeData -TypeName System.Array -ErrorAction SilentlyContinue }
            $json = ConvertTo-Json -InputObject $settings -Depth 64
            [System.IO.File]::WriteAllText($settingsPath, $json, (New-Object System.Text.UTF8Encoding($false)))
            Add-Result 'permissions' 'OK' "$($deny.Count) правил deny"
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'permissions' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 8. Запись серверов в ~/.claude.json (только если CLI claude не найден)
    # -----------------------------------------------------------------------
    if (-not $script:Claude) {
        if ($McpDefs.Count -eq 0) { Add-Result 'claude-config' 'SKIPPED' 'нечего записывать' }
        else {
            Write-Step 'Запись MCP-серверов в %USERPROFILE%\.claude.json'
            try {
                if (@(Get-Process -Name 'claude' -ErrorAction SilentlyContinue).Count -gt 0) {
                    throw 'Claude Desktop (или claude в терминале) запущен. Полностью закройте его, включая значок в трее (Quit), и запустите скрипт снова: готовые сборки пропустятся.'
                }
                Save-McpConfig (Join-Path $env:USERPROFILE '.claude.json') $McpDefs
                Add-Result 'claude-config' 'OK' ('{0} серверов, резервная копия рядом (.bak-*)' -f $McpDefs.Count)
            }
            catch { Write-Warn $_.Exception.Message; Add-Result 'claude-config' 'FAILED' $_.Exception.Message }
        }
    }

    # -----------------------------------------------------------------------
    # 9. Cursor: те же серверы в %USERPROFILE%\.cursor\mcp.json
    # -----------------------------------------------------------------------
    if (-not $WithCursor) { Add-Result 'cursor' 'SKIPPED' 'нужен -WithCursor' }
    elseif ($McpDefs.Count -eq 0) { Add-Result 'cursor' 'SKIPPED' 'нечего записывать' }
    else {
        Write-Step 'Cursor: %USERPROFILE%\.cursor\mcp.json'
        try {
            # Формат Cursor: stdio - command/args/env, удалённый сервер - url/headers.
            $cursorDefs = [ordered]@{}
            foreach ($name in $McpDefs.Keys) {
                $def = $McpDefs[$name]
                if ($def.type -eq 'http') { $cursorDefs[$name] = [ordered]@{ url = $def.url; headers = $def.headers } }
                else { $cursorDefs[$name] = [ordered]@{ type = 'stdio'; command = $def.command; args = $def.args; env = $def.env } }
            }
            Save-McpConfig (Join-Path $env:USERPROFILE '.cursor\mcp.json') $cursorDefs
            # Запреты из settings.json Claude Code в Cursor не действуют: отдельных инструментов Cursor не отключает.
            Write-Warn 'В Cursor нет запретов на отдельные инструменты. Не включайте авто-запуск для enfusion-audit и obsidian-vault, подтверждайте вызовы вручную.'
            Add-Result 'cursor' 'OK' ('{0} серверов; skills Cursor читает из ~/.claude/skills' -f $cursorDefs.Count)
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'cursor' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # Итог
    # -----------------------------------------------------------------------
    Write-Step 'Итог'
    $Results | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host
    Write-Host 'Дальше вручную:' -ForegroundColor Cyan
    Write-Host '  1. Claude Desktop: значок в трее > Quit, затем откройте снова. Во вкладке Code начните новый сеанс и проверьте серверы командой /mcp.'
    Write-Host '  2. Workbench: File > Options > General > Net API = Enabled (порт 5775) - для wb_* у enfusion-mcp.'
    Write-Host '  3. Blender: Edit > Preferences > Add-ons > включите MCP for Blender, запустите его сервер. Кнопку обновления в панели аддона не нажимайте. Официальный аддон Blender Lab одновременно не включайте: оба слушают порт 9876.'
    Write-Host '  4. Obsidian: obsidian-vault работает с файлами хранилища и без запущенного Obsidian. Для skill obsidian-cli включите CLI (Settings > General).'
    Write-Host '     Для -WithObsidian: плагин Local REST API with MCP с включённым HTTP-сервером (порт 27123). Удаление заметок и команды Obsidian запрещены.'
    Write-Host '     Cursor (-WithCursor): перезапустите Cursor, серверы появятся в Customize > MCP; skills он берёт из ~/.claude/skills.'
    Write-Host '  5. Диалог Workbench "Script Authorization Required" теперь ждёт вашего ответа. Не разрешайте Workbench.RunCmd/RunProcess из чужих модов.'
    Write-Host '     Если Claude предлагает wb_watch с action answer = yes, проверьте скрипт и метод перед подтверждением.'
    Write-Host '  6. Перед публикацией мода: wb_cleanup (enfusion-mcp) убирает скрипты-обработчики Workbench из проекта.'
    Write-Host '  7. Обновить копию Workshop-модов: перезапустите скрипт (готовые сборки пропускаются).'
    Write-Host '  8. Substance Painter: запускайте с ключом --enable-remote-scripting (Steam: Свойства > Параметры запуска).'
    Write-Host "     Файлы для MCP кладите в $AssetsRoot (meshes, resources); экспорт текстур - в export или прямо в моды Workbench."
    Write-Host '  9. Discord (-WithDiscord): приглашайте бота БЕЗ прав Administrator; роль бота - ниже ролей админов.'
    Write-Host '     Message Content Intent включайте, только если Claude должен читать текст сообщений: это канал для prompt injection.'
    Write-Host '     Изменения на сервере: перезапустите скрипт с -DiscordAllowWrites. Удаление и бан остаются пробным прогоном.'
    Write-Host "Лог: $logPath"

    if (@($Results | Where-Object { $_.Status -eq 'FAILED' }).Count -gt 0) { $script:ExitCode = 1 } else { $script:ExitCode = 0 }
}
finally {
    if ($prevOutputEncoding) { try { [Console]::OutputEncoding = $prevOutputEncoding } catch { } }
    Stop-Transcript | Out-Null
}
exit $script:ExitCode
