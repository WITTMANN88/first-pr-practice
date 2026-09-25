<#
.SYNOPSIS
    Downloads the embedded font assets for STAKEOUT (Cinzel) into Assets/Fonts.

.DESCRIPTION
    The heading font (Cinzel, SIL Open Font License 1.1) is compiled into the
    executable as a WPF <Resource>, so it renders on machines where the font is
    not installed. This script fetches the static TTF files from the upstream
    project, pinned to an immutable commit, and verifies each file's SHA-256
    before moving it into place. A tampered or truncated download is rejected.

    Idempotent: files that are already present with the expected hash are
    skipped. Rebuild the project afterwards so the fonts are embedded.

    Works in Windows PowerShell 5.1 and PowerShell 7+.

.PARAMETER Force
    Re-download every file even if a valid copy already exists.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\prepare-assets.ps1

.EXAMPLE
    pwsh ./prepare-assets.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# The Invoke-WebRequest progress bar slows Windows PowerShell 5.1 downloads by
# orders of magnitude; suppress it.
$ProgressPreference = 'SilentlyContinue'

# Windows PowerShell 5.1 on older .NET Framework configs may not offer TLS 1.2,
# which GitHub requires.
if ($PSVersionTable.PSEdition -ne 'Core') {
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

# Upstream: https://github.com/NDISCOVER/Cinzel — pinned commit, so the URLs are
# immutable and the hashes below stay valid.
$commit = 'dd598495b0fb2ad84270d5cc75d642d2f1e8eabf'
$base   = "https://raw.githubusercontent.com/NDISCOVER/Cinzel/$commit"

$assets = @(
    @{ Name = 'Cinzel-Regular.ttf'; Url = "$base/fonts/ttf/Cinzel-Regular.ttf"
       Sha256 = 'af0031129f27dc752e8629a80b793d27abea94027faa27cc660c3fc33f607a1f' }
    @{ Name = 'Cinzel-Bold.ttf';    Url = "$base/fonts/ttf/Cinzel-Bold.ttf"
       Sha256 = '0c23ec565db45c5508ee95889c60ad87debd167ca07167a43a5d68572b4e2eac' }
    # The OFL requires the license text to travel with the font.
    @{ Name = 'OFL.txt';            Url = "$base/OFL.txt"
       Sha256 = 'a46624198eeb4c2e442c38b0ff3bd8f52caeefd76675c36f410e2fb69014a239' }
)

$dest = Join-Path (Join-Path $PSScriptRoot 'Assets') 'Fonts'
New-Item -ItemType Directory -Path $dest -Force | Out-Null

function Get-Sha256Lower([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

$failed = 0
foreach ($asset in $assets) {
    $target = Join-Path $dest $asset.Name

    if (-not $Force -and (Test-Path -LiteralPath $target) -and
        (Get-Sha256Lower $target) -eq $asset.Sha256) {
        Write-Host "[skip] $($asset.Name) - present, hash OK"
        continue
    }

    # Download to a temp name first so a failed or rejected file never
    # replaces a good one.
    $tmp = "$target.download"
    try {
        Write-Host "[get ] $($asset.Name)"
        Invoke-WebRequest -Uri $asset.Url -OutFile $tmp -UseBasicParsing -TimeoutSec 60

        $actual = Get-Sha256Lower $tmp
        if ($actual -ne $asset.Sha256) {
            throw "SHA-256 mismatch: expected $($asset.Sha256), got $actual"
        }

        Move-Item -LiteralPath $tmp -Destination $target -Force
        Write-Host "[ ok ] $($asset.Name)"
    }
    catch {
        $failed++
        Write-Warning "[fail] $($asset.Name): $($_.Exception.Message)"
    }
    finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    }
}

if ($failed -gt 0) {
    Write-Warning "$failed asset(s) failed. Headings will fall back to Constantia/Georgia."
    exit 1
}

Write-Host "Fonts ready in $dest. Rebuild the project to embed them."
exit 0
