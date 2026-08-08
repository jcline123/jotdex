#Requires -Version 5.1
<#
.SYNOPSIS
  Restore a Jotdex move kit on a new Windows PC (handles .jotdexkit, .zip, or unzipped folder).

.DESCRIPTION
  Simple flow:
    1. Point this script at jotdex-move-latest.jotdexkit (or an unzipped kit folder).
    2. If the kit is encrypted, enter your Jotdex unlock password when asked.
    3. Choose install folder + vault folder — done.

.PARAMETER KitRoot
  Unzipped kit folder, OR a .zip / .jotdexkit file (default: this script's directory,
  or jotdex-move-latest.* beside the script).

.PARAMETER Password
  Unlock password for .jotdexkit (prompted if needed).

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
    [string]$Password = "",
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
    & robocopy $Source $Dest /E /COPY:DAT /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null
    $code = $LASTEXITCODE
    if ($code -ge 8) {
        throw "robocopy failed copying '$Source' -> '$Dest' (exit $code)"
    }
}

function Find-ServerExe([string]$Beside) {
    foreach ($c in @(
            (Join-Path $Beside "Jotdex.Server.exe"),
            (Join-Path $Beside "app\Jotdex.Server.exe"),
            (Join-Path (Get-Location) "Jotdex.Server.exe")
        )) {
        if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path }
    }
    return $null
}

function Expand-MoveKitArchive {
    param(
        [string]$ArchivePath,
        [string]$Password,
        [string]$WorkRoot
    )

    $ArchivePath = [System.IO.Path]::GetFullPath($ArchivePath)
    $ext = [System.IO.Path]::GetExtension($ArchivePath).ToLowerInvariant()
    $zipPath = $ArchivePath

    if ($ext -eq ".jotdexkit") {
        Write-Step "Encrypted kit — enter your Jotdex unlock password"
        if ([string]::IsNullOrWhiteSpace($Password)) {
            if ($NonInteractive) { throw "Password required for .jotdexkit in NonInteractive mode." }
            $secure = Read-Host "Jotdex unlock password" -AsSecureString
            $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
            try {
                $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
            } finally {
                [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
            }
        }

        $exe = Find-ServerExe (Split-Path -Parent $ArchivePath)
        if (-not $exe) { $exe = Find-ServerExe $scriptDir }
        if (-not $exe) {
            throw "Need Jotdex.Server.exe beside this script (or in the kit folder) to decrypt. Copy the portable exe here, then re-run."
        }

        $zipPath = Join-Path $WorkRoot "kit.zip"
        $env:JOTDEX_DECRYPT_PASSWORD = $Password
        try {
            & $exe --decrypt-kit $ArchivePath $zipPath
            if ($LASTEXITCODE -ne 0) { throw "Decrypt failed — check your password." }
        } finally {
            Remove-Item Env:\JOTDEX_DECRYPT_PASSWORD -ErrorAction SilentlyContinue
        }
        Write-Ok "Decrypted"
    }
    elseif ($ext -ne ".zip") {
        throw "Expected a .jotdexkit, .zip, or unzipped kit folder."
    }

    Write-Step "Unpacking kit"
    $extract = Join-Path $WorkRoot "extracted"
    if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $extract | Out-Null
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extract -Force

    $children = @(Get-ChildItem -LiteralPath $extract -Force)
    if ($children.Count -eq 1 -and $children[0].PSIsContainer -and -not (Test-Path -LiteralPath (Join-Path $children[0].FullName "vault"))) {
        return $children[0].FullName
    }
    if (-not (Test-Path -LiteralPath (Join-Path $extract "vault"))) {
        $found = Get-ChildItem -LiteralPath $extract -Recurse -Directory -Filter "vault" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($found) { return (Split-Path -Parent $found.FullName) }
        throw "Unpacked kit has no vault\ folder."
    }
    return $extract
}

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$workTemp = $null

if ([string]::IsNullOrWhiteSpace($KitRoot)) {
    $KitRoot = $scriptDir
}

$KitRoot = [System.IO.Path]::GetFullPath($KitRoot)

if ((Test-Path -LiteralPath $KitRoot -PathType Container) -and -not (Test-Path -LiteralPath (Join-Path $KitRoot "vault"))) {
    $latest = @(
        Get-ChildItem -LiteralPath $KitRoot -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^jotdex-move.*\.(jotdexkit|zip)$' } |
            Sort-Object LastWriteTime -Descending
    ) | Select-Object -First 1
    if ($latest) {
        Write-Host "Using kit file: $($latest.FullName)"
        $KitRoot = $latest.FullName
    }
}

if (Test-Path -LiteralPath $KitRoot -PathType Leaf) {
    $workTemp = Join-Path $env:TEMP ("jotdex-restore-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $workTemp | Out-Null
    try {
        $KitRoot = Expand-MoveKitArchive -ArchivePath $KitRoot -Password $Password -WorkRoot $workTemp
    } catch {
        if ($workTemp -and (Test-Path -LiteralPath $workTemp)) {
            Remove-Item -LiteralPath $workTemp -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

$vaultSrc = Join-Path $KitRoot "vault"
$appSrc = Join-Path $KitRoot "app"
$appdataSrc = Join-Path $KitRoot "appdata"
$manifestPath = Join-Path $KitRoot "MANIFEST.json"

Write-Host "Jotdex restore (move kit)" -ForegroundColor White
Write-Host "Kit: $KitRoot"

if (-not (Test-Path -LiteralPath $vaultSrc)) {
    throw "This does not look like a move kit (missing vault\). Point -KitRoot at a .jotdexkit / .zip / unzipped folder."
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

try {
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
        $portableLoose = Join-Path $appdataSrc "secrets-portable.json"
        if (Test-Path -LiteralPath $portableLoose) {
            $secDir = Join-Path $dataDest "secrets"
            New-Item -ItemType Directory -Force -Path $secDir | Out-Null
            Copy-Item -LiteralPath $portableLoose -Destination (Join-Path $secDir "secrets-portable.json") -Force
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
    Write-Host "  4. Unlock with your existing password (and authenticator code if TOTP is on)"
    Write-Host "  5. Search rebuilds automatically; notification secrets import on first start"
    Write-Host ""
    if (Test-Path -LiteralPath (Join-Path $dataDest "secrets\secrets-portable.json")) {
        Write-Host "Note: secrets-portable.json will be imported into DPAPI on first launch, then removed." -ForegroundColor Yellow
    }
    if (Test-Path -LiteralPath $manifestPath) {
        Write-Host "Manifest: $manifestPath"
    }
}
finally {
    if ($workTemp -and (Test-Path -LiteralPath $workTemp)) {
        try { Remove-Item -LiteralPath $workTemp -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    }
}
