<#
.SYNOPSIS
  Ultimate Game Optimizer :: auto-detects an installed game by name and applies
  the full optimization pack (NVIDIA profile, engine config, CPU priority)
  without any manual file paths.

.DESCRIPTION
  Target hardware: Intel Core i5-12450H / NVIDIA RTX 3060 Laptop 6GB / 16GB RAM.

  Pipeline:
    1. Find-GameInstallDir  - locate the install folder from a Steam library,
                              falling back to the Windows Uninstall registry.
    2. Find-GameExecutable  - pick the real runtime .exe inside that folder
                              (Unreal ships "<Project>-Win64-Shipping.exe",
                              not the friendly game name).
    3. Get-EngineInfo       - detect Unreal (+ version) / Unity / Other from
                              on-disk signatures, and locate the Unreal
                              project's Engine.ini when applicable.
    4. New-NvidiaProfile + Import-NvidiaProfile
                            - build the .nip and import it via
                              nvidiaProfileInspector.exe's command-line import.
    5. Set-IniValue         - merge the Scalability/SystemSettings keys into
                              the real Engine.ini, preserving everything else
                              already in the file.
    6. Set-CpuPriority      - back up the current IFEO PerfOptions value to
                              undo_<exe>.reg, then set CpuPriorityClass=3 (High).

.PARAMETER GameName
  Partial or full name of the game, as it appears in your Steam library
  folder or Windows "Programs and Features" list (e.g. "Wardogs").

.PARAMETER WhatIf
  Detect and print everything that would be changed, without touching the
  registry, the NVIDIA profile store, or any game file.

.EXAMPLE
  .\AutoOptimize.ps1 -GameName "Wardogs"

.EXAMPLE
  .\AutoOptimize.ps1 -GameName "Wardogs" -WhatIf
#>

#Requires -RunAsAdministrator

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameName,

    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "[*] $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "[+] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[!] $msg" -ForegroundColor Yellow }

# ---------------------------------------------------------------------------
# 1. Locate the game install directory
# ---------------------------------------------------------------------------

function Get-SteamLibraries {
    $steamPath = $null
    foreach ($key in 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $prop = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($prop.InstallPath) { $steamPath = $prop.InstallPath; break }
    }
    if (-not $steamPath) { return @() }

    $libraries = [System.Collections.Generic.List[string]]::new()
    $libraries.Add($steamPath)

    $vdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $content = Get-Content $vdf -Raw
        foreach ($m in [regex]::Matches($content, '"path"\s+"([^"]+)"')) {
            $libraries.Add(($m.Groups[1].Value -replace '\\\\', '\'))
        }
    }
    return $libraries | Select-Object -Unique
}

function Find-GameInstallDir {
    param([string]$Name)

    $candidates = [System.Collections.Generic.List[string]]::new()

    # Steam: <library>\steamapps\common\<GameFolder>
    foreach ($lib in Get-SteamLibraries) {
        $common = Join-Path $lib 'steamapps\common'
        if (Test-Path $common) {
            Get-ChildItem -Path $common -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like "*$Name*" } |
                ForEach-Object { $candidates.Add($_.FullName) }
        }
    }

    # Fallback: Windows "Programs and Features" (Uninstall) registry
    if ($candidates.Count -eq 0) {
        $uninstallKeys = @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )
        foreach ($key in $uninstallKeys) {
            Get-ItemProperty -Path $key -ErrorAction SilentlyContinue |
                Where-Object { $_.DisplayName -like "*$Name*" -and $_.InstallLocation } |
                ForEach-Object { $candidates.Add($_.InstallLocation) }
        }
    }

    return $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
}

function Find-GameExecutable {
    param([string]$InstallDir, [string]$Name)

    $excludePattern = 'unins|redist|vcredist|dxsetup|crashreport|battleye|easyanticheat|directx'

    Get-ChildItem -Path $InstallDir -Filter '*.exe' -Recurse -Depth 6 -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -notmatch $excludePattern } |
        Sort-Object -Property @(
            @{ Expression = { if ($_.BaseName -like "*$($Name -replace '\s', '')*") { 0 } else { 1 } } },
            @{ Expression = 'Length'; Descending = $true }
        ) |
        Select-Object -First 1
}

# ---------------------------------------------------------------------------
# 2. Detect the engine and, for Unreal, the real Engine.ini path
# ---------------------------------------------------------------------------

function Get-EngineInfo {
    param([System.IO.FileInfo]$Exe)

    $dir = $Exe.DirectoryName

    # Walk up looking for a .uproject (Unreal) - the exe usually sits under
    # <Project>\Binaries\Win64\, several levels below the project root.
    $projectRoot = $dir
    $uproject = $null
    for ($i = 0; $i -lt 6; $i++) {
        $found = Get-ChildItem -Path $projectRoot -Filter '*.uproject' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $uproject = $found; break }
        $parent = Split-Path $projectRoot -Parent
        if (-not $parent -or $parent -eq $projectRoot) { break }
        $projectRoot = $parent
    }

    if ($uproject) {
        $version = 5
        try {
            $json = Get-Content $uproject.FullName -Raw | ConvertFrom-Json
            if ($json.EngineAssociation -match '^4') { $version = 4 }
        } catch { }

        return [pscustomobject]@{
            Engine       = "Unreal Engine $version"
            IsUE5        = ($version -eq 5)
            ProjectRoot  = $projectRoot
            EngineIniDir = Join-Path $projectRoot 'Saved\Config\WindowsNoEditor'
        }
    }

    $base = $Exe.BaseName
    if (Test-Path (Join-Path $dir "${base}_Data\Managed\UnityEngine.dll")) {
        return [pscustomobject]@{ Engine = 'Unity'; IsUE5 = $false; ProjectRoot = $dir; EngineIniDir = $null }
    }

    return [pscustomobject]@{ Engine = 'Other'; IsUE5 = $false; ProjectRoot = $dir; EngineIniDir = $null }
}

# ---------------------------------------------------------------------------
# 3. NVIDIA profile (.nip) - build + import
# ---------------------------------------------------------------------------

function New-NvidiaProfile {
    param([string]$ExeName)

    $base = [IO.Path]::GetFileNameWithoutExtension($ExeName) -replace '[^a-zA-Z0-9_\-]', '_'

    return @"
<?xml version="1.0" encoding="utf-8"?>
<!-- Ultimate Game Optimizer :: NVIDIA Profile Inspector export -->
<!-- Target executable: $ExeName | Hardware: RTX 3060 Laptop 6GB / i5-12450H -->
<ArrayOfProfile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Profile>
    <ProfileName>${base}_UGO_Optimized</ProfileName>
    <Executeables>
      <string>$ExeName</string>
    </Executeables>
    <Settings>
      <ProfileSetting>
        <SettingNameInfo>Maximum pre-rendered frames</SettingNameInfo>
        <SettingId>8464993</SettingId>
        <SettingValue>1</SettingValue>
        <ValueType>Integer</ValueType>
      </ProfileSetting>
      <ProfileSetting>
        <SettingNameInfo>Ultra Low Latency - CPL State</SettingNameInfo>
        <SettingId>277041152</SettingId>
        <SettingValue>1</SettingValue>
        <ValueType>Integer</ValueType>
      </ProfileSetting>
      <ProfileSetting>
        <SettingNameInfo>Texture filtering - Quality</SettingNameInfo>
        <SettingId>14019156</SettingId>
        <SettingValue>32</SettingValue>
        <ValueType>Integer</ValueType>
      </ProfileSetting>
      <ProfileSetting>
        <SettingNameInfo>Threaded optimization</SettingNameInfo>
        <SettingId>284249078</SettingId>
        <SettingValue>1</SettingValue>
        <ValueType>Integer</ValueType>
      </ProfileSetting>
      <ProfileSetting>
        <SettingNameInfo>Power management mode</SettingNameInfo>
        <SettingId>287253157</SettingId>
        <SettingValue>1</SettingValue>
        <ValueType>Integer</ValueType>
      </ProfileSetting>
    </Settings>
  </Profile>
</ArrayOfProfile>
"@
}

function Find-NvidiaProfileInspector {
    $candidates = @(
        "$Env:ProgramFiles\NVIDIA Corporation\Profile Inspector\nvidiaProfileInspector.exe",
        "${Env:ProgramFiles(x86)}\NVIDIA Corporation\Profile Inspector\nvidiaProfileInspector.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    $cmd = Get-Command 'nvidiaProfileInspector.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $found = Get-ChildItem -Path $Env:ProgramFiles, ${Env:ProgramFiles(x86)}, $Env:LOCALAPPDATA -Filter 'nvidiaProfileInspector.exe' -Recurse -Depth 3 -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { return $found.FullName }

    return $null
}

function Import-NvidiaProfile {
    param([string]$NipContent, [string]$ExeName)

    $nipPath = Join-Path $Env:TEMP ("{0}.nip" -f ([IO.Path]::GetFileNameWithoutExtension($ExeName)))
    Set-Content -Path $nipPath -Value $NipContent -Encoding UTF8

    $inspector = Find-NvidiaProfileInspector
    if (-not $inspector) {
        Write-Warn "nvidiaProfileInspector.exe not found automatically."
        Write-Warn "Profile saved to $nipPath - import it manually (double-click, or drag onto the app)."
        return $false
    }

    Write-Step "Importing NVIDIA profile via $inspector ..."
    Start-Process -FilePath $inspector -ArgumentList "`"$nipPath`"" -Wait
    Write-Ok "NVIDIA profile imported for $ExeName."
    return $true
}

# ---------------------------------------------------------------------------
# 4. Engine config - merge into the real Engine.ini, keep everything else
# ---------------------------------------------------------------------------

function Set-IniValue {
    param([string]$Path, [string]$Section, [System.Collections.Specialized.OrderedDictionary]$Values)

    $lines = if (Test-Path $Path) { @(Get-Content -Path $Path -Encoding UTF8) } else { @() }

    $output = [System.Collections.Generic.List[string]]::new()
    $inSection = $false
    $sectionFound = $false
    $handled = @{}

    # Inserts any not-yet-seen keys right after the section's last real line,
    # before any trailing blank lines, instead of at the very end of $output.
    $flushPending = {
        if (-not $inSection) { return }
        $insertAt = $output.Count
        while ($insertAt -gt 0 -and $output[$insertAt - 1].Trim() -eq '') { $insertAt-- }
        $newKeys = @($Values.Keys | Where-Object { -not $handled.ContainsKey($_) })
        for ($j = $newKeys.Count - 1; $j -ge 0; $j--) {
            $output.Insert($insertAt, "$($newKeys[$j])=$($Values[$newKeys[$j]])")
        }
        foreach ($k in $newKeys) { $handled[$k] = $true }
    }

    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        if ($trimmed -match '^\[(.+)\]$') {
            & $flushPending
            $inSection = ($Matches[1] -eq $Section)
            if ($inSection) { $sectionFound = $true }
            $output.Add($line)
            continue
        }

        if ($inSection -and $trimmed -match '^([^=]+)=') {
            $key = $Matches[1].Trim()
            if ($Values.Contains($key)) {
                $output.Add("$key=$($Values[$key])")
                $handled[$key] = $true
                continue
            }
        }
        $output.Add($line)
    }
    & $flushPending

    if (-not $sectionFound) {
        if ($output.Count -gt 0) { $output.Add('') }
        $output.Add("[$Section]")
        foreach ($k in $Values.Keys) { $output.Add("$k=$($Values[$k])") }
    }

    $dir = Split-Path $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
    Set-Content -Path $Path -Value $output -Encoding UTF8
}

function Set-UnrealConfig {
    param([string]$EngineIniDir, [bool]$IsUE5)

    $engineIni = Join-Path $EngineIniDir 'Engine.ini'

    $scalability = [ordered]@{
        'sg.ViewDistanceQuality'        = 2
        'sg.AntiAliasingQuality'        = 1
        'sg.TextureQuality'             = 1
        'sg.PostProcessQuality'         = 0
        'sg.ShadowQuality'              = 1
        'sg.ReflectionQuality'          = 0
        'sg.GlobalIlluminationQuality'  = 0
    }
    $system = [ordered]@{
        'r.TextureStreaming'              = 1
        'r.Streaming.PoolSize'            = 3072
        'r.Streaming.LimitPoolSizeToVRAM' = 1
        'r.AsyncCompute'                  = 1
    }
    if ($IsUE5) {
        $system['r.Lumen.HardwareRayTracing']    = 0
        $system['r.Lumen.DiffuseIndirect.Allow'] = 0
        $system['r.Lumen.Reflections.Allow']     = 0
        $system['r.Shadow.Virtual.Enable']       = 0
    }

    Set-IniValue -Path $engineIni -Section 'ScalabilityGroups' -Values $scalability
    Set-IniValue -Path $engineIni -Section 'SystemSettings' -Values $system

    return $engineIni
}

# ---------------------------------------------------------------------------
# 5. CPU priority - backup then apply
# ---------------------------------------------------------------------------

function Set-CpuPriority {
    param([string]$ExeName)

    $ifeoPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\$ExeName"
    $perfPath = Join-Path $ifeoPath 'PerfOptions'
    $undoFile = Join-Path (Get-Location) ("undo_" + $ExeName + ".reg")

    Write-Step "Backing up current PerfOptions for $ExeName ..."

    if (Test-Path $perfPath) {
        $existing = Get-ItemProperty -Path $perfPath -ErrorAction SilentlyContinue
        if ($null -ne $existing.CpuPriorityClass) {
            $hexValue = "{0:x8}" -f [int]$existing.CpuPriorityClass
            $lines = @(
                'Windows Registry Editor Version 5.00', '',
                "[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\$ExeName\PerfOptions]",
                ('"CpuPriorityClass"=dword:' + $hexValue)
            )
        } else {
            $lines = @(
                'Windows Registry Editor Version 5.00', '',
                "[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\$ExeName\PerfOptions]",
                '"CpuPriorityClass"=-'
            )
        }
    } else {
        $lines = @(
            'Windows Registry Editor Version 5.00', '',
            "[-HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\$ExeName]"
        )
    }
    $lines | Out-File -FilePath $undoFile -Encoding unicode
    Write-Ok "Rollback saved to $undoFile"

    Write-Step "Applying High priority (CpuPriorityClass=3) for $ExeName ..."
    New-Item -Path $ifeoPath -Force | Out-Null
    New-Item -Path $perfPath -Force | Out-Null
    New-ItemProperty -Path $perfPath -Name 'CpuPriorityClass' -PropertyType DWord -Value 3 -Force | Out-Null
    Write-Ok "$ExeName now launches with High CPU priority."
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

Write-Step "Searching for an installed game matching '$GameName' ..."
$installDirs = Find-GameInstallDir -Name $GameName
if ($installDirs.Count -eq 0) {
    Write-Warn "No installed game matching '$GameName' was found in any Steam library or in Programs and Features."
    Write-Warn "Run the game once via its launcher, or pass the exact Steam library folder name."
    exit 1
}
$installDir = $installDirs | Select-Object -First 1
Write-Ok "Install directory: $installDir"

$exe = Find-GameExecutable -InstallDir $installDir -Name $GameName
if (-not $exe) {
    Write-Warn "Found the install folder but no runnable .exe inside it. Aborting."
    exit 1
}
Write-Ok "Executable: $($exe.Name)"

$engineInfo = Get-EngineInfo -Exe $exe
Write-Ok "Engine: $($engineInfo.Engine)"

Write-Host ''
Write-Host "=== Plan for $($exe.Name) ($($engineInfo.Engine)) ===" -ForegroundColor Magenta
Write-Host "  1. Import NVIDIA profile (.nip) - Max pre-rendered frames=1, Ultra Low Latency=On, Texture filtering=High Perf, Threaded opt=On, Power mode=Max Perf"
if ($engineInfo.Engine -like 'Unreal*') {
    Write-Host "  2. Merge ScalabilityGroups + SystemSettings into: $($engineInfo.EngineIniDir)\Engine.ini"
} elseif ($engineInfo.Engine -eq 'Unity') {
    Write-Host "  2. Unity detected - MaxBulletHoleDecals/VolumetricLighting live in per-title PlayerPrefs/json and vary by game, so they are not auto-written. Manual steps will be printed."
} else {
    Write-Host "  2. No engine-specific config template for '$($engineInfo.Engine)' - only the NVIDIA profile and CPU priority will be applied."
}
Write-Host "  3. Set CpuPriorityClass=3 (High) for $($exe.Name), with rollback saved to undo_$($exe.Name).reg"
Write-Host ''

if ($WhatIf) {
    Write-Warn "Dry run (-WhatIf) - nothing was changed."
    exit 0
}

$nip = New-NvidiaProfile -ExeName $exe.Name
Import-NvidiaProfile -NipContent $nip -ExeName $exe.Name | Out-Null

if ($engineInfo.Engine -like 'Unreal*') {
    $iniPath = Set-UnrealConfig -EngineIniDir $engineInfo.EngineIniDir -IsUE5 $engineInfo.IsUE5
    Write-Ok "Engine.ini updated: $iniPath"
} elseif ($engineInfo.Engine -eq 'Unity') {
    Write-Warn "Add launch argument:  -window-mode exclusive -screen-fullscreen 1"
    Write-Warn "Then manually set MaxBulletHoleDecals=50 and disable Volumetric Lighting in one of these HKCU\Software subkeys:"
    Get-ChildItem 'HKCU:\Software' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -notin @('Microsoft', 'Classes', 'Policies', 'RegisteredApplications') } |
        ForEach-Object { Write-Host "      - $($_.PSChildName)" -ForegroundColor Yellow }
} else {
    Write-Warn "No auto-applicable engine config for '$($engineInfo.Engine)'."
}

Set-CpuPriority -ExeName $exe.Name

Write-Host ''
Write-Ok "Optimization pack applied for $GameName ($($exe.Name))."
