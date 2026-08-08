#Requires -Version 5.1
<#
.SYNOPSIS
  Build a full Jotdex move kit ZIP from a portable publish + vault/data paths.

.DESCRIPTION
  Use when Settings → Create move kit cannot embed the app (e.g. you are on `dotnet run`).
  Publishes win-x64 if needed, then packs app + vault + appdata + Restore-Jotdex.ps1.

.PARAMETER VaultPath
  Live vault to include.

.PARAMETER DataRoot
  App data root (config/auth/history). Default: artifacts\win-x64\data if present.

.PARAMETER OutputDir
  Where to write jotdex-move-*.zip (default: DataRoot\exports\backups or .\artifacts).

.PARAMETER SkipPublish
  Do not run publish-win-x64.ps1.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VaultPath,
    [string]$DataRoot = "",
    [string]$OutputDir = "",
    [string]$AppDir = "",
    [switch]$SkipPublish,
    [bool]$IncludeAuth = $true,
    [bool]$IncludeHistory = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish-win-x64.ps1"
$defaultApp = Join-Path $repo "artifacts\win-x64"

if (-not $SkipPublish) {
    Write-Host "Publishing portable build..."
    & $publishScript
}

if ([string]::IsNullOrWhiteSpace($AppDir)) { $AppDir = $defaultApp }
$AppDir = [System.IO.Path]::GetFullPath($AppDir)
if (-not (Test-Path -LiteralPath (Join-Path $AppDir "Jotdex.Server.exe"))) {
    throw "Portable app not found at $AppDir - run publish or pass -AppDir."
}

$VaultPath = [System.IO.Path]::GetFullPath($VaultPath)
if (-not (Test-Path -LiteralPath $VaultPath)) { throw "Vault not found: $VaultPath" }

if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $cand = Join-Path $AppDir "data"
    if (Test-Path -LiteralPath $cand) { $DataRoot = $cand }
    else { $DataRoot = Join-Path $repo "src\Server\data" }
}
$DataRoot = [System.IO.Path]::GetFullPath($DataRoot)

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $DataRoot "exports\backups"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$stage = Join-Path $env:TEMP "jotdex-move-stage-$stamp"
$zipPath = Join-Path $OutputDir "jotdex-move-$stamp.zip"

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Copy-Tree([string]$Source, [string]$Dest) {
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    & robocopy $Source $Dest /E /COPY:DAT /R:1 /W:1 /XD data /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed $Source -> $Dest ($LASTEXITCODE)" }
}

Write-Host "Staging kit..."
Copy-Tree $VaultPath (Join-Path $stage "vault")

# App without nested data/
$appDest = Join-Path $stage "app"
New-Item -ItemType Directory -Force -Path $appDest | Out-Null
Get-ChildItem -LiteralPath $AppDir -Force | Where-Object { $_.Name -ne "data" } | ForEach-Object {
    $dest = Join-Path $appDest $_.Name
    if ($_.PSIsContainer) {
        & robocopy $_.FullName $dest /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $($_.Name)" }
    } else {
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    }
}

$appdata = Join-Path $stage "appdata"
New-Item -ItemType Directory -Force -Path $appdata | Out-Null
foreach ($name in @("config", $(if ($IncludeAuth) { "auth" } else { $null }), $(if ($IncludeHistory) { "history" } else { $null }))) {
    if (-not $name) { continue }
    $src = Join-Path $DataRoot $name
    if (Test-Path -LiteralPath $src) {
        Copy-Tree $src (Join-Path $appdata $name)
    }
}

# Prefer already-exported portable secrets (from a prior in-app move kit / manual export).
# Raw DPAPI secrets.json will not work on another PC — do not copy it.
$portableSecrets = Join-Path $DataRoot "secrets\secrets-portable.json"
if (Test-Path -LiteralPath $portableSecrets) {
    $secDest = Join-Path $appdata "secrets"
    New-Item -ItemType Directory -Force -Path $secDest | Out-Null
    Copy-Item -LiteralPath $portableSecrets -Destination (Join-Path $secDest "secrets-portable.json") -Force
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Restore-Jotdex.ps1") -Destination (Join-Path $stage "Restore-Jotdex.ps1") -Force

$readme = @"
Jotdex move kit
===============
Unzip and run Restore-Jotdex.ps1 on the new PC.
See docs/backup.md in the Jotdex repo for details.
For SMTP/Telegram/TOTP secrets, prefer Settings → Backup → Create move kit
(it unwraps DPAPI). This CLI kit only includes secrets-portable.json if that file already exists.
"@
Set-Content -LiteralPath (Join-Path $stage "README-MOVE.txt") -Value $readme -Encoding UTF8

$manifest = [ordered]@{
    kind           = "jotdex-move-kit"
    createdUtc     = (Get-Date).ToUniversalTime().ToString("o")
    vaultPath      = $VaultPath
    includedApp    = $true
    includeAuth    = [bool]$IncludeAuth
    includeHistory = [bool]$IncludeHistory
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage "MANIFEST.json") -Encoding UTF8

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Remove-Item -LiteralPath $stage -Recurse -Force
$len = (Get-Item -LiteralPath $zipPath).Length
Write-Host "Done: $zipPath ($([math]::Round($len/1MB,1)) MB)"
