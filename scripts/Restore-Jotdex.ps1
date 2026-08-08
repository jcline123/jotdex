#Requires -Version 5.1
<#
.SYNOPSIS
  Restore a Jotdex move kit on a new Windows PC.

.DESCRIPTION
  Run this from the unzipped move-kit folder. Prompts for:
    - Install folder (portable program + .\data)
    - Vault folder (live notes on local disk — not iCloud)

  Copies app\, vault\, and appdata\; rewrites vault.json; disables stale cloud mirror paths.

.PARAMETER KitRoot
  Unzipped kit folder (default: this script's directory).

.PARAMETER InstallPath
  Where to place Jotdex (default: prompted, e.g. C:\Jotdex).

.PARAMETER VaultPath
  Live vault destination (default: prompted, e.g. C:\JotdexVault).

.PARAMETER Force
  Overwrite existing install/vault folders without asking.

.PARAMETER NonInteractive
  Use parameters only; fail if required paths missing.
#>
[CmdletBinding()]
param(
    [string]$KitRoot = "",
    [string]$InstallPath = "",
    [string]$VaultPath = "",
    [switch]$Force,
    [switch]$NonInteractive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host "    OK  $Message" -ForegroundColor Green
}

function Write-WarnLine([string]$Message) {
    Write-Host "    !!  $Message" -ForegroundColor Yellow
}

function Confirm-Yes([string]$Prompt, [bool]$DefaultYes = $false) {
    if ($NonInteractive) { return $DefaultYes }
    if ($Force) { return $true }
    $suffix = if ($DefaultYes) { "[Y/n]" } else { "[y/N]" }
    $answer = Read-Host "$Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $DefaultYes }
    return $answer -match '^(y|yes)$'
}

function Test-LooksLikeICloudLive([string]$Path) {
    $p = $Path.ToLowerInvariant().Replace('/', '\')
    return ($p -match 'icloud') -or ($p -match '\\onedrive\\') -or ($p -match '\\dropbox\\')
}

function Copy-Tree([string]$Source, [string]$Dest) {
    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Missing source: $Source"
    }
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    # robocopy exit codes 0-7 are success
    & robocopy $Source $Dest /E /COPY:DAT /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null
    $code = $LASTEXITCODE
    if ($code -ge 8) {
        throw "robocopy failed copying '$Source' -> '$Dest' (exit $code)"
    }
}

if ([string]::IsNullOrWhiteSpace($KitRoot)) {
    $KitRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$KitRoot = [System.IO.Path]::GetFullPath($KitRoot)

$vaultSrc = Join-Path $KitRoot "vault"
$appSrc = Join-Path $KitRoot "app"
$appdataSrc = Join-Path $KitRoot "appdata"
$manifestPath = Join-Path $KitRoot "MANIFEST.json"

Write-Host "Jotdex restore (move kit)" -ForegroundColor White
Write-Host "Kit: $KitRoot"

if (-not (Test-Path -LiteralPath $vaultSrc)) {
    throw "This does not look like a move kit (missing vault\). Unzip the jotdex-move-*.zip first, then run this script from that folder."
}

$includedApp = Test-Path -LiteralPath (Join-Path $appSrc "Jotdex.Server.exe")
if (-not $includedApp) {
    Write-WarnLine "No portable app\ in this kit. You must already have (or download) a Jotdex portable build."
    Write-WarnLine "InstallPath will still receive appdata + you must place Jotdex.Server.exe there yourself, or re-create the kit from the portable build."
}

if ([string]::IsNullOrWhiteSpace($InstallPath)) {
    if ($NonInteractive) { throw "InstallPath required in NonInteractive mode." }
    $InstallPath = Read-Host "Install folder for Jotdex (program + data) [C:\Jotdex]"
    if ([string]::IsNullOrWhiteSpace($InstallPath)) { $InstallPath = "C:\Jotdex" }
}
$InstallPath = [System.IO.Path]::GetFullPath($InstallPath)

if ([string]::IsNullOrWhiteSpace($VaultPath)) {
    if ($NonInteractive) { throw "VaultPath required in NonInteractive mode." }
    $VaultPath = Read-Host "Live vault folder (local disk, not iCloud) [C:\JotdexVault]"
    if ([string]::IsNullOrWhiteSpace($VaultPath)) { $VaultPath = "C:\JotdexVault" }
}
$VaultPath = [System.IO.Path]::GetFullPath($VaultPath)

if (Test-LooksLikeICloudLive $VaultPath) {
    Write-WarnLine "Vault path looks like cloud sync (iCloud/OneDrive/Dropbox)."
    Write-WarnLine "Jotdex must use a local-disk vault; cloud folders are for read-only mirrors only."
    if (-not (Confirm-Yes "Continue anyway? (not recommended)" $false)) {
        throw "Aborted. Choose a local path such as C:\JotdexVault."
    }
}

if ((Test-Path -LiteralPath $InstallPath) -and -not $Force) {
    $hasFiles = @(Get-ChildItem -LiteralPath $InstallPath -Force -ErrorAction SilentlyContinue).Count -gt 0
    if ($hasFiles -and -not (Confirm-Yes "Install folder exists and is not empty: $InstallPath — overwrite/merge?" $false)) {
        throw "Aborted."
    }
}

if ((Test-Path -LiteralPath $VaultPath) -and -not $Force) {
    $hasVault = @(Get-ChildItem -LiteralPath $VaultPath -Force -ErrorAction SilentlyContinue).Count -gt 0
    if ($hasVault -and -not (Confirm-Yes "Vault folder already has files: $VaultPath — overwrite/merge?" $false)) {
        throw "Aborted."
    }
}

Write-Step "Copying vault"
Copy-Tree $vaultSrc $VaultPath
Write-Ok $VaultPath

$dataDest = Join-Path $InstallPath "data"
Write-Step "Preparing install folder"
New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
New-Item -ItemType Directory -Force -Path $dataDest | Out-Null

if ($includedApp) {
    Write-Step "Copying portable app"
    Copy-Tree $appSrc $InstallPath
    Write-Ok "Jotdex.Server.exe -> $InstallPath"
} else {
    Write-WarnLine "Skipped app copy (not in kit)."
}

if (Test-Path -LiteralPath $appdataSrc) {
    Write-Step "Copying app data (auth, config, history)"
    Get-ChildItem -LiteralPath $appdataSrc -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $dest = Join-Path $dataDest $_.Name
        Copy-Tree $_.FullName $dest
        Write-Ok $_.Name
    }
}

Write-Step "Pointing config at the new vault path"
$configDir = Join-Path $dataDest "config"
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$vaultJson = Join-Path $configDir "vault.json"
$vaultObj = @{ vaultPath = $VaultPath }
$vaultObj | ConvertTo-Json | Set-Content -LiteralPath $vaultJson -Encoding UTF8
Write-Ok $vaultJson

$mirrorJson = Join-Path $configDir "vault-mirror.json"
if (Test-Path -LiteralPath $mirrorJson) {
    try {
        $mirror = Get-Content -LiteralPath $mirrorJson -Raw | ConvertFrom-Json
        $changed = $false
        if ($mirror.PSObject.Properties.Name -contains "enabled" -and $mirror.enabled) {
            $mirror.enabled = $false
            $changed = $true
        }
        if ($mirror.PSObject.Properties.Name -contains "destinationPath") {
            $mirror.destinationPath = ""
            $changed = $true
        }
        if ($changed) {
            $mirror | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $mirrorJson -Encoding UTF8
            Write-WarnLine "Disabled cloud mirror settings (old PC path). Re-enable in Settings if needed."
        }
    } catch {
        Write-WarnLine "Could not adjust vault-mirror.json: $($_.Exception.Message)"
    }
}

# Ensure portable mode beside install
$example = Join-Path $InstallPath "appsettings.json"
if (-not (Test-Path -LiteralPath $example)) {
    @"
{
  "Jotdex": {
    "VaultPath": "",
    "DataRoot": "",
    "PortableMode": true
  }
}
"@ | Set-Content -LiteralPath $example -Encoding UTF8
}

Write-Step "Done"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Green
Write-Host "  1. cd `"$InstallPath`""
Write-Host "  2. Double-click start-portable.cmd  (or run .\Jotdex.Server.exe)"
Write-Host "  3. Open http://127.0.0.1:5180"
Write-Host "  4. Unlock with your existing password (if you had one)"
Write-Host "  5. Search rebuilds automatically on first start"
Write-Host ""
if (Test-Path -LiteralPath $manifestPath) {
    Write-Host "Manifest: $manifestPath"
}
