#Requires -Version 5.0
<#
.SYNOPSIS
    Clean temporary files and reclaim disk space on Windows.

.PARAMETER DryRun
    Show what would be removed, without deleting anything.

.PARAMETER Yes
    Skip the confirmation prompt.

.EXAMPLE
    .\cleanup.ps1 -DryRun
    .\cleanup.ps1 -Yes
#>

param(
    [switch]$DryRun,
    [switch]$Yes
)

function Get-FreeSpaceReport {
    $drive = Get-PSDrive -Name (Get-Location).Drive.Name
    $freeGb = [math]::Round($drive.Free / 1GB, 2)
    $usedGb = [math]::Round($drive.Used / 1GB, 2)
    "$freeGb GB free / $usedGb GB used"
}

function Remove-PathContents {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path $Path)) {
        return
    }

    Write-Host "Cleaning $Label ($Path)..."
    Get-ChildItem -Path $Path -Recurse -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            if ($DryRun) {
                Write-Host "[dry-run] Remove: $($_.FullName)"
            } else {
                Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
}

Write-Host "Disk space before: $(Get-FreeSpaceReport)"
Write-Host ""

if (-not $Yes -and -not $DryRun) {
    $reply = Read-Host "This will delete temporary files and caches. Continue? [y/N]"
    if ($reply -notmatch '^[Yy]$') {
        Write-Host "Aborted."
        exit 0
    }
}

Remove-PathContents -Path $env:TEMP -Label "user temp folder"
Remove-PathContents -Path "$env:WINDIR\Temp" -Label "Windows temp folder"
Remove-PathContents -Path "$env:LOCALAPPDATA\Microsoft\Windows\INetCache" -Label "IE/Edge cache"
Remove-PathContents -Path "$env:LOCALAPPDATA\Temp" -Label "local app temp"

$edgeCache = "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Default\Cache"
Remove-PathContents -Path $edgeCache -Label "Edge browser cache"

$chromeCache = "$env:LOCALAPPDATA\Google\Chrome\User Data\Default\Cache"
Remove-PathContents -Path $chromeCache -Label "Chrome browser cache"

Write-Host "Emptying Recycle Bin..."
if ($DryRun) {
    Write-Host "[dry-run] Clear-RecycleBin -Force"
} else {
    Clear-RecycleBin -Force -ErrorAction SilentlyContinue
}

Write-Host "Running Disk Cleanup (cleanmgr) with default settings..."
if ($DryRun) {
    Write-Host "[dry-run] cleanmgr /sagerun:1"
} else {
    if (Get-Command cleanmgr -ErrorAction SilentlyContinue) {
        Start-Process cleanmgr -ArgumentList "/sagerun:1" -Wait -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "Disk space after: $(Get-FreeSpaceReport)"
