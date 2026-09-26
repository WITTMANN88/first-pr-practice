<#
.SYNOPSIS
    Downloads the embedded font assets for STAKEOUT (Cinzel, Cormorant SC) into Assets/Fonts.

.DESCRIPTION
    The heading fonts (SIL Open Font License 1.1) are compiled into the
    executable as WPF <Resource>s, so they render on machines where the fonts
    are not installed: Cinzel for Latin, and Cormorant SC for Cyrillic, which
    Cinzel lacks (WPF falls back per character, see Themes/Styles.xaml).
    This script fetches the static TTF files from immutable, versioned URLs
    and verifies each file's SHA-256 before moving it into place. A tampered or
    truncated download is rejected.

    Cormorant SC's license text (Assets/Fonts/OFL-CormorantSC.txt) is kept in
    the repository: Google Fonts serves it only inside its download bundle,
    which has no immutable URL to pin.

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
# which both hosts require.
if ($PSVersionTable.PSEdition -ne 'Core') {
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

# Upstream: https://github.com/NDISCOVER/Cinzel — pinned commit, so the URLs are
# immutable and the hashes below stay valid.
$commit = 'dd598495b0fb2ad84270d5cc75d642d2f1e8eabf'
$base   = "https://raw.githubusercontent.com/NDISCOVER/Cinzel/$commit"
$gstatic = 'https://fonts.gstatic.com/s/cormorantsc/v19'

$assets = @(
    @{ Name = 'Cinzel-Regular.ttf'; Url = "$base/fonts/ttf/Cinzel-Regular.ttf"
       Sha256 = 'af0031129f27dc752e8629a80b793d27abea94027faa27cc660c3fc33f607a1f' }
    @{ Name = 'Cinzel-Bold.ttf';    Url = "$base/fonts/ttf/Cinzel-Bold.ttf"
       Sha256 = '0c23ec565db45c5508ee95889c60ad87debd167ca07167a43a5d68572b4e2eac' }
    # The OFL requires the license text to travel with the font.
    @{ Name = 'OFL.txt';            Url = "$base/OFL.txt"
       Sha256 = 'a46624198eeb4c2e442c38b0ff3bd8f52caeefd76675c36f410e2fb69014a239' }

    # Cormorant SC (Cyrillic headings), Google Fonts release v19: the version is
    # part of the URL, so the files behind it do not change.
    @{ Name = 'CormorantSC-Regular.ttf'
       Url = "$gstatic/0yb5GD4kxqXBmOVLG30OGwserDow9Tbu-Q.ttf"
       Sha256 = 'd3d8c2ca6a8fdf47e38d05b6aeb0b62e6116321719f680e7a7e3a42710d6d1e8' }
    @{ Name = 'CormorantSC-Bold.ttf'
       Url = "$gstatic/0ybmGD4kxqXBmOVLG30OGwsmEBUU_R3y8DOWGA.ttf"
       Sha256 = '56f7ed2188fe7bde701359e8fb7cb44c6d1a023655dc25884d132855457f797f' }
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
