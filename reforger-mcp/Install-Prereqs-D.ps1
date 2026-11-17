#Requires -Version 5.1
<#
.SYNOPSIS
    Установка программ для Install-ReforgerMcp.ps1 на диск D: (Windows 10/11).

.DESCRIPTION
    Ставит в D:\Apps (меняется параметром -AppsRoot):
      Git            - winget Git.Git (хеш установщика проверяет winget по своему манифесту)
      Node.js        - 22.23.5 LTS, zip с nodejs.org, SHA256 сверяется
      uv             - 0.13.9, пакет с PyPI, SHA256 сверяется
      VS Build Tools - компилятор C++ для Rust, загрузчик Microsoft с проверкой цифровой подписи
      Rust           - rustup 1.29.3, SHA256 сверяется; RUSTUP_HOME и CARGO_HOME на D:
      Blender        - 4.5.16 LTS (под Enfusion Blender Tools), zip с blender.org, SHA256 сверяется
      Obsidian       - winget Obsidian.Obsidian
    Кэши npm и uv и Python, который скачивает uv, тоже переносятся на D:.
    Arma Reforger, Arma Reforger Tools и Substance 3D Painter ставятся через Steam/Adobe:
    с -OpenSteam скрипт откроет окно установки Steam для игры и Tools (выберите библиотеку на D:).

    Хеши проверены 2026-11-17: файлы скачаны и пересчитаны, контрольные суммы взяты
    с самих nodejs.org, pypi.org, static.rust-lang.org и download.blender.org.

.NOTES
    Запуск (обычный пользователь; Git и VS Build Tools сами попросят права администратора):
        Unblock-File .\Install-Prereqs-D.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Prereqs-D.ps1
    Затем в НОВОМ окне PowerShell (чтобы подхватился PATH):
        .\Install-ReforgerMcp.ps1 -InstallRoot D:\mcp -AssetsRoot D:\ReforgerAssets
    Файл должен оставаться в кодировке UTF-8 с BOM. Повторный запуск безопасен.
#>
[CmdletBinding()]
param(
    [string]$AppsRoot = 'D:\Apps',
    [switch]$SkipGit,
    [switch]$SkipNode,
    [switch]$SkipUv,
    [switch]$SkipBuildTools,
    [switch]$SkipRust,
    [switch]$SkipBlender,
    [switch]$SkipObsidian,
    [switch]$OpenSteam
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---------------------------------------------------------------------------
# Проверенные загрузки
# ---------------------------------------------------------------------------
$Downloads = @{
    Node    = @{ Version = '22.23.5'; Url = 'https://nodejs.org/dist/v22.23.5/node-v22.23.5-win-x64.zip'; Sha256 = '4a191082e9706866830379d577d2cc8a50c776fcd9436a8d061b063ed0d9a0e3' }
    Uv      = @{ Version = '0.13.9'; Url = 'https://files.pythonhosted.org/packages/dd/ef/ceeabf3631117f7354787d9fb1ffe943899e59ed4b887a696ed547e47e76/uv-0.13.9-py3-none-win_amd64.whl'; Sha256 = '6a44b2ed8111d2f60d2b92627c5dccda3f7ed7570b18ce115e569cd68c28246b' }
    Rustup  = @{ Version = '1.29.3'; Url = 'https://static.rust-lang.org/rustup/archive/1.29.3/x86_64-pc-windows-msvc/rustup-init.exe'; Sha256 = '38027d51d69a5d05493627a0634dbea52d6cd793c8f072d2373d3f4163eec7db' }
    Blender = @{ Version = '4.5.16'; Url = 'https://download.blender.org/release/Blender4.5/blender-4.5.16-windows-x64.zip'; Sha256 = 'e258e90a1ebc14798ad65b3426bc5f11f5e0e013541c61cc7c3ee9bbf4ca0ce6' }
}
# Загрузчик VS Build Tools меняется вместе с выпусками, поэтому вместо хеша проверяется подпись Microsoft.
$BuildToolsUrl = 'https://aka.ms/vs/17/release/vs_BuildTools.exe'
# Steam: Arma Reforger и Arma Reforger Tools (SteamDB).
$SteamApps = [ordered]@{ 'Arma Reforger' = 1874880; 'Arma Reforger Tools' = 1874910 }

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

function Resolve-Exe([string]$Name) {
    $cmd = Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    return $null
}

# Запуск внешней программы: вывод построчно, ненулевой код = исключение.
function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [switch]$AllowFail,
        [switch]$Quiet
    )
    $lines = New-Object System.Collections.Generic.List[string]
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $FilePath @ArgumentList 2>&1 | ForEach-Object {
            $line = [string]$_
            $lines.Add($line)
            if (-not $Quiet) { Write-Host "    $line" -ForegroundColor DarkGray }
        }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prevEap }
    if ($code -ne 0 -and -not $AllowFail) {
        throw ('Команда завершилась с кодом {0}: {1} {2}' -f $code, $FilePath, ($ArgumentList -join ' '))
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $lines.ToArray() }
}

# Скачивание с проверкой SHA256. При несовпадении файл удаляется.
function Save-Download([string]$Url, [string]$Path, [string]$Sha256) {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    Write-Host "    загрузка: $Url"
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (Test-Path -LiteralPath $curl) {
        # Без перенаправления stderr: так виден прогресс, и Windows PowerShell 5.1 не считает его ошибкой.
        & $curl -fL --retry 3 --progress-bar -o $Path $Url
        if ($LASTEXITCODE -ne 0) { throw "Загрузка не удалась (curl, код $LASTEXITCODE): $Url" }
    }
    else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
    }
    if ($Sha256) {
        $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        if ($actual -ne $Sha256) {
            Remove-Item -LiteralPath $Path -Force
            throw "SHA256 не совпал для $Url (ожидался $Sha256, получен $actual). Файл удалён."
        }
        Write-Ok 'SHA256 совпадает'
    }
}

function Add-UserPath([string]$Dir) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    try {
        # Без раскрытия %VAR%: существующие записи PATH сохраняются как есть.
        $raw = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $parts = @($raw -split ';' | Where-Object { $_ })
        if (-not ($parts | Where-Object { $_.TrimEnd('\') -ieq $Dir.TrimEnd('\') })) {
            $key.SetValue('Path', (($parts + $Dir) -join ';'), [Microsoft.Win32.RegistryValueKind]::ExpandString)
            Write-Ok "PATH пользователя: + $Dir"
        }
    }
    finally { $key.Close() }
    if (-not (($env:Path -split ';') -contains $Dir)) { $env:Path = "$env:Path;$Dir" }
}

function Set-UserEnv([string]$Name, [string]$Value) {
    # SetEnvironmentVariable(User) заодно оповещает систему, и новые окна видят изменения.
    [Environment]::SetEnvironmentVariable($Name, $Value, 'User')
    Set-Item -Path "Env:$Name" -Value $Value
    Write-Ok "$Name = $Value"
}

$VsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
function Get-VsInstances([string[]]$Requires = @()) {
    if (-not (Test-Path -LiteralPath $VsWhere)) { return @() }
    $vsArgs = @('-products', '*', '-property', 'installationPath')
    if ($Requires.Count -gt 0) { $vsArgs = @('-products', '*', '-requires') + $Requires + @('-property', 'installationPath') }
    return @((Invoke-Native $VsWhere $vsArgs -AllowFail -Quiet).Output | Where-Object { $_ -and (Test-Path -LiteralPath $_) })
}
function Test-MsvcTools { return ((Get-VsInstances @('Microsoft.VisualStudio.Component.VC.Tools.x86.x64')).Count -gt 0) }

function Expand-ZipTo([string]$Zip, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($Zip, $Destination)
}

# ---------------------------------------------------------------------------
# 0. Окружение
# ---------------------------------------------------------------------------
if ($env:OS -ne 'Windows_NT') { throw 'Скрипт рассчитан на Windows 10/11.' }
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$AppsRoot = [System.IO.Path]::GetFullPath($AppsRoot).TrimEnd('\')
$drive = Split-Path -Qualifier $AppsRoot
if (-not (Test-Path -LiteralPath "$drive\")) { throw "Диск $drive не найден." }
New-Item -ItemType Directory -Path $AppsRoot -Force | Out-Null
$Tmp = Join-Path $AppsRoot '_downloads'
New-Item -ItemType Directory -Path $Tmp -Force | Out-Null

$logPath = Join-Path $AppsRoot ('prereqs-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))
Start-Transcript -Path $logPath | Out-Null

try {
    Write-Step "Проверка окружения ($AppsRoot)"
    $free = (Get-PSDrive -Name $drive.TrimEnd(':')).Free
    $freeGb = [math]::Round($free / 1GB, 1)
    if ($freeGb -lt 20) { Write-Warn "На $drive свободно $freeGb ГБ. Для всего набора нужно около 20 ГБ (больше всего занимают VS Build Tools и Rust)." }
    else { Write-Ok "Свободно на ${drive}: $freeGb ГБ" }
    $identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warn 'Скрипт запущен от администратора: PATH и переменные запишутся этому администратору. Лучше запускать от своего пользователя.'
    }
    $winget = Resolve-Exe 'winget'
    if (-not $winget) { Write-Warn 'winget не найден: Git и Obsidian не поставятся. Установите App Installer из Microsoft Store.' }

    # -----------------------------------------------------------------------
    # 1. Git
    # -----------------------------------------------------------------------
    if ($SkipGit) { Add-Result 'git' 'SKIPPED' '-SkipGit' }
    elseif (Resolve-Exe 'git') { Add-Result 'git' 'OK' ('уже установлен: {0}' -f (Resolve-Exe 'git')) }
    elseif (-not $winget) { Add-Result 'git' 'FAILED' 'нет winget' }
    else {
        Write-Step 'Git (winget)'
        try {
            $null = Invoke-Native $winget @('install', '--id', 'Git.Git', '-e', '--source', 'winget', '--location', (Join-Path $AppsRoot 'Git'),
                '--silent', '--accept-package-agreements', '--accept-source-agreements')
            # Установщик Git сам прописывает системный PATH; дублируем только если он поставился именно сюда.
            $gitCmd = Join-Path $AppsRoot 'Git\cmd'
            if (Test-Path -LiteralPath (Join-Path $gitCmd 'git.exe')) { Add-UserPath $gitCmd }
            Add-Result 'git' 'OK' (Join-Path $AppsRoot 'Git')
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'git' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 2. Node.js (portable zip)
    # -----------------------------------------------------------------------
    if ($SkipNode) { Add-Result 'node' 'SKIPPED' '-SkipNode' }
    else {
        Write-Step "Node.js $($Downloads.Node.Version) LTS"
        try {
            $nodeDir = Join-Path $AppsRoot 'nodejs'
            $nodeExe = Join-Path $nodeDir 'node.exe'
            $marker = Join-Path $nodeDir '.installed-by-prereqs'
            $current = if (Test-Path -LiteralPath $nodeExe) { ([string]((Invoke-Native $nodeExe @('--version') -Quiet).Output | Select-Object -First 1)).Trim() } else { '' }
            if ($current -eq "v$($Downloads.Node.Version)") {
                Write-Ok "уже установлен: $nodeExe"
            }
            else {
                if ((Test-Path -LiteralPath $nodeDir) -and -not (Test-Path -LiteralPath $marker)) {
                    throw "Папка $nodeDir уже есть и создана не этим скриптом. Переименуйте её или укажите другой -AppsRoot."
                }
                $zip = Join-Path $Tmp 'node.zip'
                Save-Download $Downloads.Node.Url $zip $Downloads.Node.Sha256
                $extract = Join-Path $Tmp 'node-extract'
                if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
                Expand-ZipTo $zip $extract
                if (Test-Path -LiteralPath $nodeDir) { Remove-Item -LiteralPath $nodeDir -Recurse -Force }
                Move-Item -LiteralPath (Join-Path $extract "node-v$($Downloads.Node.Version)-win-x64") -Destination $nodeDir
                Set-Content -LiteralPath $marker -Value $Downloads.Node.Version -Encoding ASCII
                Remove-Item -LiteralPath $zip, $extract -Recurse -Force
            }
            Add-UserPath $nodeDir
            Set-UserEnv 'npm_config_cache' (Join-Path $AppsRoot 'cache\npm')
            # Системный PATH Windows идёт раньше пользовательского: старый Node из Program Files перекроет новый.
            $first = Get-Command -Name node -CommandType Application -All -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($first -and ($first.Source -ine $nodeExe)) {
                Write-Warn "Первым в PATH стоит другой Node: $($first.Source). Удалите его (Параметры > Приложения) или поставьте $nodeDir выше в системном PATH."
            }
            Add-Result 'node' 'OK' $nodeDir
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'node' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 3. uv (из пакета PyPI берутся только uv.exe, uvx.exe, uvw.exe)
    # -----------------------------------------------------------------------
    if ($SkipUv) { Add-Result 'uv' 'SKIPPED' '-SkipUv' }
    else {
        Write-Step "uv $($Downloads.Uv.Version)"
        try {
            $uvDir = Join-Path $AppsRoot 'uv'
            $uvExe = Join-Path $uvDir 'uv.exe'
            $current = if (Test-Path -LiteralPath $uvExe) { [string]((Invoke-Native $uvExe @('--version') -Quiet).Output | Select-Object -First 1) } else { '' }
            if ($current -match [regex]::Escape($Downloads.Uv.Version)) {
                Write-Ok "уже установлен: $uvExe"
            }
            else {
                $wheel = Join-Path $Tmp 'uv.whl'
                Save-Download $Downloads.Uv.Url $wheel $Downloads.Uv.Sha256
                New-Item -ItemType Directory -Path $uvDir -Force | Out-Null
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $archive = [System.IO.Compression.ZipFile]::OpenRead($wheel)
                try {
                    $exes = @($archive.Entries | Where-Object { $_.FullName -like 'uv-*.data/scripts/*.exe' })
                    if ($exes.Count -eq 0) { throw 'В пакете uv не найдено uv.exe.' }
                    foreach ($entry in $exes) {
                        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $uvDir $entry.Name), $true)
                    }
                }
                finally { $archive.Dispose() }
                Remove-Item -LiteralPath $wheel -Force
            }
            Add-UserPath $uvDir
            Set-UserEnv 'UV_CACHE_DIR' (Join-Path $AppsRoot 'cache\uv')
            Set-UserEnv 'UV_PYTHON_INSTALL_DIR' (Join-Path $AppsRoot 'uv\python')
            Add-Result 'uv' 'OK' $uvDir
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'uv' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 4. Visual Studio Build Tools (C++), нужны Rust
    # -----------------------------------------------------------------------
    if ($SkipBuildTools) { Add-Result 'vs-buildtools' 'SKIPPED' '-SkipBuildTools' }
    elseif (Test-MsvcTools) { Add-Result 'vs-buildtools' 'OK' 'C++ уже установлен' }
    else {
        Write-Step 'Visual Studio Build Tools (C++)'
        try {
            $bootstrapper = Join-Path $Tmp 'vs_BuildTools.exe'
            Save-Download $BuildToolsUrl $bootstrapper ''
            $sig = Get-AuthenticodeSignature -LiteralPath $bootstrapper
            $signer = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { 'без подписи' }
            if ($sig.Status -ne 'Valid' -or $signer -notmatch 'O=Microsoft Corporation') {
                Remove-Item -LiteralPath $bootstrapper -Force
                throw "Подпись загрузчика не прошла проверку ($($sig.Status), $signer). Файл удалён."
            }
            Write-Ok 'Подпись Microsoft действительна'
            $argLine = '--installPath "{0}" --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive --norestart --wait' -f (Join-Path $AppsRoot 'VSBuildTools')
            # Общие компоненты и кэш VS можно перенести только при самой первой установке Visual Studio.
            if ((Get-VsInstances).Count -eq 0) {
                $argLine += ' --path cache="{0}" --path shared="{1}"' -f (Join-Path $AppsRoot 'VSCache'), (Join-Path $AppsRoot 'VSShared')
            }
            Write-Warn 'Установщик попросит права администратора и покажет окно прогресса. Windows SDK всё равно ставится на C: (около 2-3 ГБ).'
            $proc = Start-Process -FilePath $bootstrapper -ArgumentList $argLine -Wait -PassThru
            if ($proc.ExitCode -eq 3010) { Write-Warn 'Нужна перезагрузка: перезагрузите ПК и запустите скрипт снова.' }
            elseif ($proc.ExitCode -ne 0) { throw "VS Build Tools: код завершения $($proc.ExitCode)." }
            Remove-Item -LiteralPath $bootstrapper -Force
            Add-Result 'vs-buildtools' 'OK' (Join-Path $AppsRoot 'VSBuildTools')
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'vs-buildtools' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 5. Rust (rustup)
    # -----------------------------------------------------------------------
    if ($SkipRust) { Add-Result 'rust' 'SKIPPED' '-SkipRust' }
    elseif (Resolve-Exe 'cargo') { Add-Result 'rust' 'OK' ('уже установлен: {0}' -f (Resolve-Exe 'cargo')) }
    elseif (-not (Test-MsvcTools)) {
        # Без C++ Build Tools rustup -y сам предложит ставить Visual Studio Community - не допускаем этого.
        Write-Warn 'Rust пропущен: нет C++ Build Tools. Поставьте их (без -SkipBuildTools) и запустите скрипт снова.'
        Add-Result 'rust' 'SKIPPED' 'нет C++ Build Tools'
    }
    else {
        Write-Step "Rust (rustup $($Downloads.Rustup.Version))"
        try {
            $rustRoot = Join-Path $AppsRoot 'rust'
            Set-UserEnv 'RUSTUP_HOME' (Join-Path $rustRoot 'rustup')
            Set-UserEnv 'CARGO_HOME' (Join-Path $rustRoot 'cargo')
            $rustupInit = Join-Path $Tmp 'rustup-init.exe'
            Save-Download $Downloads.Rustup.Url $rustupInit $Downloads.Rustup.Sha256
            $null = Invoke-Native $rustupInit @('-y', '--profile', 'minimal', '--default-toolchain', 'stable')
            Add-UserPath (Join-Path $rustRoot 'cargo\bin')
            Remove-Item -LiteralPath $rustupInit -Force
            Add-Result 'rust' 'OK' $rustRoot
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'rust' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 6. Blender 4.5 LTS (portable)
    # -----------------------------------------------------------------------
    if ($SkipBlender) { Add-Result 'blender' 'SKIPPED' '-SkipBlender' }
    else {
        Write-Step "Blender $($Downloads.Blender.Version) LTS"
        try {
            $blenderRoot = Join-Path $AppsRoot 'Blender'
            $versionDir = Join-Path $blenderRoot "blender-$($Downloads.Blender.Version)-windows-x64"
            if (-not (Test-Path -LiteralPath (Join-Path $versionDir 'blender.exe'))) {
                if (Test-Path -LiteralPath $versionDir) { throw "Папка $versionDir есть, но в ней нет blender.exe. Удалите её и запустите скрипт снова." }
                New-Item -ItemType Directory -Path $blenderRoot -Force | Out-Null
                $zip = Join-Path $Tmp 'blender.zip'
                Save-Download $Downloads.Blender.Url $zip $Downloads.Blender.Sha256
                Write-Host '    распаковка (около 1 ГБ, может занять несколько минут)'
                Expand-ZipTo $zip $blenderRoot
                Remove-Item -LiteralPath $zip -Force
            }
            $blenderExe = Join-Path $versionDir 'blender.exe'
            if (-not (Test-Path -LiteralPath $blenderExe)) {
                $found = Get-ChildItem -LiteralPath $blenderRoot -Filter 'blender.exe' -Recurse -Depth 1 | Select-Object -First 1
                if (-not $found) { throw "blender.exe не найден в $blenderRoot." }
                $blenderExe = $found.FullName
            }
            $shell = New-Object -ComObject WScript.Shell
            $lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Blender 4.5 LTS.lnk'))
            $lnk.TargetPath = $blenderExe
            $lnk.WorkingDirectory = Split-Path -Parent $blenderExe
            $lnk.Save()
            Write-Ok 'Ярлык в меню Пуск: Blender 4.5 LTS'
            # Папка пользовательских аддонов: в неё Install-ReforgerMcp.ps1 скопирует аддон MCP for Blender.
            New-Item -ItemType Directory -Path (Join-Path $env:APPDATA 'Blender Foundation\Blender\4.5\scripts\addons') -Force | Out-Null
            Add-Result 'blender' 'OK' $blenderExe
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'blender' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 7. Obsidian
    # -----------------------------------------------------------------------
    $obsidianDir = Join-Path $AppsRoot 'Obsidian'
    $obsidianKnown = @((Join-Path $obsidianDir 'Obsidian.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Obsidian\Obsidian.exe')) | Where-Object { Test-Path -LiteralPath $_ }
    if ($SkipObsidian) { Add-Result 'obsidian' 'SKIPPED' '-SkipObsidian' }
    elseif (@($obsidianKnown).Count -gt 0) { Add-Result 'obsidian' 'OK' ('уже установлен: {0}' -f @($obsidianKnown)[0]) }
    elseif (-not $winget) { Add-Result 'obsidian' 'FAILED' 'нет winget' }
    else {
        Write-Step 'Obsidian (winget)'
        try {
            $null = Invoke-Native $winget @('install', '--id', 'Obsidian.Obsidian', '-e', '--source', 'winget', '--location', $obsidianDir,
                '--silent', '--accept-package-agreements', '--accept-source-agreements')
            Add-Result 'obsidian' 'OK' $obsidianDir
        }
        catch { Write-Warn $_.Exception.Message; Add-Result 'obsidian' 'FAILED' $_.Exception.Message }
    }

    # -----------------------------------------------------------------------
    # 8. Steam: игра и Tools
    # -----------------------------------------------------------------------
    if ($OpenSteam) {
        Write-Step 'Steam: Arma Reforger и Arma Reforger Tools'
        foreach ($name in $SteamApps.Keys) {
            Start-Process ('steam://install/{0}' -f $SteamApps[$name])
            Write-Ok "$name - окно установки Steam открыто, выберите библиотеку на диске $drive"
        }
        Add-Result 'steam' 'OK' 'окна установки открыты'
    }
    else { Add-Result 'steam' 'SKIPPED' 'нужен -OpenSteam' }

    # -----------------------------------------------------------------------
    # Итог
    # -----------------------------------------------------------------------
    Write-Step 'Итог'
    $Results | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host
    Write-Host 'Дальше:' -ForegroundColor Cyan
    Write-Host '  1. Закройте это окно и Claude Desktop (значок в трее > Quit): новые PATH и переменные видны только новым программам.'
    Write-Host "  2. Steam > Настройки > Хранилище: добавьте библиотеку на $drive и установите туда Arma Reforger и Arma Reforger Tools (или запустите скрипт с -OpenSteam)."
    Write-Host '     Substance 3D Painter ставится через Steam или Adobe Creative Cloud: там тоже выберите диск D:.'
    Write-Host '  3. В НОВОМ окне PowerShell:'
    Write-Host '     powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-ReforgerMcp.ps1 -InstallRoot D:\mcp -AssetsRoot D:\ReforgerAssets -WithCursor -WithDiscord'
    Write-Host "Лог: $logPath"

    if (@($Results | Where-Object { $_.Status -eq 'FAILED' }).Count -gt 0) { $script:ExitCode = 1 } else { $script:ExitCode = 0 }
}
finally {
    Stop-Transcript | Out-Null
}
exit $script:ExitCode
