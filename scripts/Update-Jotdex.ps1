#Requires -Version 5.1
<#
.SYNOPSIS
  Update a portable Jotdex install from a GitHub Release (with automatic program backup + rollback).

.DESCRIPTION
  1. Stops Jotdex in the install folder
  2. Backs up program files (not data\ / vault) to C:\JotdexBackupHold\...
  3. Downloads the latest (or specified) win-x64 release zip from GitHub
  4. Replaces program files, keeps data\
  5. Starts Jotdex and waits for /api/health
  6. Asks you to confirm the app looks OK (window stays open)
  7. If you say no (or health fails), restores the backup and restarts

.PARAMETER InstallPath
  Portable install folder (default: folder containing this script).

.PARAMETER Repo
  GitHub owner/name (default: jcline123/jotdex).

.PARAMETER BackupHold
  Where to store program backups (default: C:\JotdexBackupHold).

.PARAMETER ZipPath
  Use a local zip instead of downloading (testing).

.PARAMETER SkipConfirm
  Assume success after healthy start (not recommended).

.PARAMETER HealthUrl
  Health check URL (default http://127.0.0.1:5180/api/health).
#>
[CmdletBinding()]
param(
    [string]$InstallPath = "",
    [string]$Repo = "jcline123/jotdex",
    [string]$BackupHold = "C:\JotdexBackupHold",
    [string]$ZipPath = "",
    [string]$HealthUrl = "http://127.0.0.1:5180/api/health",
    [switch]$SkipConfirm,
    [switch]$Force
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

function Confirm-Yes([string]$Prompt, [bool]$DefaultYes = $true) {
    if ($SkipConfirm) { return $DefaultYes }
    $suffix = if ($DefaultYes) { "[Y/n]" } else { "[y/N]" }
    $answer = Read-Host "$Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $DefaultYes }
    return $answer -match '^(y|yes)$'
}

function Stop-JotdexIn([string]$Dir) {
    $exe = Join-Path $Dir "Jotdex.Server.exe"
    Get-Process -Name "Jotdex.Server" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and ([string]::Equals($_.Path, $exe, [StringComparison]::OrdinalIgnoreCase))) {
                Write-Host "    Stopping PID $($_.Id)..."
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            }
        } catch { }
    }
    Start-Sleep -Seconds 2
}

function Start-JotdexIn([string]$Dir) {
    $cmd = Join-Path $Dir "start-portable.cmd"
    $exe = Join-Path $Dir "Jotdex.Server.exe"
    if (Test-Path -LiteralPath $cmd) {
        Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "`"$cmd`"" -WorkingDirectory $Dir -WindowStyle Minimized
    } elseif (Test-Path -LiteralPath $exe) {
        Start-Process -FilePath $exe -WorkingDirectory $Dir -WindowStyle Minimized
    } else {
        throw "Cannot start Jotdex - missing start-portable.cmd / Jotdex.Server.exe in $Dir"
    }
}

function Wait-Health([string]$Url, [int]$Seconds = 60) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-RestMethod -Uri $Url -TimeoutSec 3
            if ($r.status -eq "ok") { return $true }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Copy-ProgramTree([string]$Source, [string]$Dest) {
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | Where-Object { $_.Name -ne "data" } | ForEach-Object {
        $target = Join-Path $Dest $_.Name
        if ($_.PSIsContainer) {
            & robocopy $_.FullName $target /E /COPY:DAT /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $($_.Name) ($LASTEXITCODE)" }
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

function Restore-FromBackup([string]$BackupDir, [string]$InstallDir) {
    Write-Step "Restoring previous program from backup"
    Stop-JotdexIn $InstallDir
    # Remove program files except data\
    Get-ChildItem -LiteralPath $InstallDir -Force | Where-Object { $_.Name -ne "data" } | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    Copy-ProgramTree $BackupDir $InstallDir
    Start-JotdexIn $InstallDir
    if (Wait-Health $HealthUrl 90) {
        Write-Ok "Restored and healthy"
    } else {
        Write-WarnLine "Restored files, but health check did not succeed yet. Start start-portable.cmd manually."
    }
}

if ([string]::IsNullOrWhiteSpace($InstallPath)) {
    $InstallPath = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$InstallPath = [System.IO.Path]::GetFullPath($InstallPath)
$exePath = Join-Path $InstallPath "Jotdex.Server.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Jotdex.Server.exe not found in $InstallPath. Run this script from your portable install folder."
}

Write-Host "Jotdex updater" -ForegroundColor White
Write-Host "Install: $InstallPath"

$backupStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = Join-Path $BackupHold "jotdex-prog-$backupStamp"
$work = Join-Path $env:TEMP "jotdex-update-$backupStamp"
$downloadedZip = Join-Path $work "release.zip"
$extractDir = Join-Path $work "extract"

try {
    Write-Step "Backing up current program to $backupDir"
    New-Item -ItemType Directory -Force -Path $BackupHold | Out-Null
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Stop-JotdexIn $InstallPath
    Copy-ProgramTree $InstallPath $backupDir
    Write-Ok "Backup ready (data\ and vault were not copied - they stay in place)"

    if ([string]::IsNullOrWhiteSpace($ZipPath)) {
        Write-Step "Looking up latest GitHub release for $Repo"
        $headers = @{
            "User-Agent" = "Jotdex-Updater"
            "Accept"     = "application/vnd.github+json"
        }
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers
        if (-not $release -or -not $release.tag_name) {
            throw "No GitHub release found. Publish a Release with a portable win-x64 zip asset."
        }
        Write-Ok "Latest release: $($release.tag_name)"
        $asset = @($release.assets) | Where-Object {
            $_.name -match 'win-x64' -or $_.name -match 'portable' -or $_.name -match '^jotdex.*\.zip$'
        } | Select-Object -First 1
        if (-not $asset) {
            throw "Release $($release.tag_name) has no zip asset (expected name containing win-x64 or portable). Upload artifacts\win-x64 as a zip on the Release."
        }
        Write-Step "Downloading $($asset.name)"
        New-Item -ItemType Directory -Force -Path $work | Out-Null
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $downloadedZip -Headers $headers -UseBasicParsing
        $ZipPath = $downloadedZip
    } else {
        $ZipPath = [System.IO.Path]::GetFullPath($ZipPath)
        if (-not (Test-Path -LiteralPath $ZipPath)) { throw "Zip not found: $ZipPath" }
        Write-Ok "Using local zip: $ZipPath"
    }

    Write-Step "Extracting update"
    if (Test-Path -LiteralPath $extractDir) { Remove-Item $extractDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $extractDir -Force

    # Zip may contain files at root or a single top folder
    $payload = $extractDir
    $children = @(Get-ChildItem -LiteralPath $extractDir -Force)
    if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
        $payload = $children[0].FullName
    }
    if (-not (Test-Path -LiteralPath (Join-Path $payload "Jotdex.Server.exe"))) {
        # search one level
        $found = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter "Jotdex.Server.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $payload = Split-Path -Parent $found.FullName }
        else { throw "Update zip does not contain Jotdex.Server.exe" }
    }

    Write-Step "Applying update (keeping data\)"
    Get-ChildItem -LiteralPath $InstallPath -Force | Where-Object { $_.Name -ne "data" } | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    Copy-ProgramTree $payload $InstallPath
    # Ensure updater script remains available
    $self = $MyInvocation.MyCommand.Path
    if ($self -and (Test-Path -LiteralPath $self)) {
        Copy-Item -LiteralPath $self -Destination (Join-Path $InstallPath "Update-Jotdex.ps1") -Force -ErrorAction SilentlyContinue
    }
    # Ensure cloud-backup / app data folders exist on older installs (do not wipe contents).
    $dataRoot = Join-Path $InstallPath "data"
    foreach ($rel in @(
            "config",
            "secrets",
            "state\cloud-backup",
            "exports\backups",
            "exports\cloud-backup-staging"
        )) {
        New-Item -ItemType Directory -Force -Path (Join-Path $dataRoot $rel) | Out-Null
    }
    Write-Ok "Files replaced"

    Write-Step "Starting Jotdex"
    Start-JotdexIn $InstallPath
    $healthy = Wait-Health $HealthUrl 90
    if (-not $healthy) {
        Write-WarnLine "Health check failed after update."
        if (Confirm-Yes "Restore the previous program from backup?" $true) {
            Restore-FromBackup $backupDir $InstallPath
            throw "Update rolled back due to failed health check."
        }
        throw "Update applied but health check failed. Backup is at $backupDir"
    }
    Write-Ok "Server responded OK at $HealthUrl"

    Write-Host ""
    Write-Host "Open Jotdex in your browser and spot-check (home, a note, Settings)." -ForegroundColor Yellow
    $ok = Confirm-Yes "Does everything look good? Keep this update" $true
    if (-not $ok) {
        Restore-FromBackup $backupDir $InstallPath
        Write-Host ""
        Write-Host "Rolled back to the pre-update program. Backup kept at:" -ForegroundColor Yellow
        Write-Host "  $backupDir"
        exit 2
    }

    Write-Host ""
    Write-Host "Update complete." -ForegroundColor Green
    Write-Host "Program backup (keep awhile, then delete if happy):" -ForegroundColor Green
    Write-Host "  $backupDir"
    Write-Host "Press Enter to close this window."
    if (-not $SkipConfirm) { [void](Read-Host) }
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ((Test-Path -LiteralPath $backupDir) -and (Confirm-Yes "Restore backup now?" $true)) {
        try { Restore-FromBackup $backupDir $InstallPath } catch { Write-WarnLine $_.Exception.Message }
    }
    Write-Host "Press Enter to close this window."
    if (-not $SkipConfirm) { [void](Read-Host) }
    exit 1
}
finally {
    if (Test-Path -LiteralPath $work) {
        try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    }
}
