#Requires -Version 5.1
<#
.SYNOPSIS
  Guided first-time setup for Jotdex on Windows.

.DESCRIPTION
  Walks through checking/installing Git, .NET SDK, and Node.js (via winget when available),
  cloning or using this repo, creating a vault folder, building the portable app, and optionally
  starting Jotdex / adding a Startup shortcut.

  Security notes:
  - Does NOT download random EXEs from the web. Optional installs use Windows Package Manager (winget).
  - Asks before every install. Does not disable antivirus or change global PowerShell policy.
  - Prefer:  .\Setup.cmd   or   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Setup-Jotdex.ps1

.PARAMETER VaultPath
  Notes folder path. Default prompted (C:\JotdexVault).

.PARAMETER SkipDeps
  Do not offer to install missing tools.

.PARAMETER SkipBuild
  Skip npm/dotnet publish (use existing artifacts\win-x64 if present).

.PARAMETER Start
  Start Jotdex when setup completes.

.PARAMETER NonInteractive
  Use defaults / parameters only (still fails if required tools are missing unless -SkipDeps and tools exist).
#>
[CmdletBinding()]
param(
    [string]$VaultPath = "",
    [string]$RepoRoot = "",
    [switch]$SkipDeps,
    [switch]$SkipBuild,
    [switch]$Start,
    [switch]$NonInteractive,
    [switch]$AddStartupShortcut
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

function Write-Info([string]$Message) {
    Write-Host "    $Message"
}

function Confirm-Yes([string]$Prompt, [bool]$DefaultYes = $true) {
    if ($NonInteractive) { return $DefaultYes }
    $suffix = if ($DefaultYes) { "[Y/n]" } else { "[y/N]" }
    $answer = Read-Host "$Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $DefaultYes }
    return $answer -match '^(y|yes)$'
}

function Refresh-Path {
    $machine = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $user = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = @($machine, $user) -join ";"
}

function Test-Command([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-DotNetSdkMajor {
    if (-not (Test-Command "dotnet")) { return 0 }
    try {
        $line = (& dotnet --list-sdks 2>$null | Select-Object -Last 1)
        if (-not $line) {
            $ver = (& dotnet --version 2>$null)
            if ($ver -match '^(\d+)') { return [int]$Matches[1] }
            return 0
        }
        if ($line -match '^(\d+)') { return [int]$Matches[1] }
    } catch { }
    return 0
}

function Test-Winget {
    return (Test-Command "winget")
}

function Install-WithWinget([string]$PackageId, [string]$DisplayName) {
    if (-not (Test-Winget)) {
        Write-WarnLine "winget is not available. Install $DisplayName manually, then re-run setup."
        Write-Info "Microsoft Store -> App Installer, or https://aka.ms/getwinget"
        return $false
    }

    Write-Info "About to run (you can cancel the UAC / winget prompt if shown):"
    Write-Host "      winget install -e --id $PackageId --accept-package-agreements --accept-source-agreements" -ForegroundColor DarkGray
    if (-not (Confirm-Yes "Install $DisplayName with winget now?" $true)) {
        Write-WarnLine "Skipped $DisplayName."
        return $false
    }

    $wingetArgs = @(
        "install", "-e", "--id", $PackageId,
        "--accept-package-agreements",
        "--accept-source-agreements"
    )
    & winget @wingetArgs
    Refresh-Path
    return $true
}

function Assert-NotCloudSyncPath([string]$Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $lower = $full.ToLowerInvariant()
    $bad = @(
        "\iclouddrive\",
        "\icloud drive\",
        "\onedrive\",
        "\dropbox\",
        "\google drive\"
    )
    foreach ($b in $bad) {
        if ($lower.Contains($b)) {
            throw "Vault path looks like a cloud sync folder ($Path). Use a local disk path (e.g. C:\JotdexVault). You can enable Cloud backup mirror later inside Jotdex."
        }
    }
}

function Resolve-RepoRoot {
    if ($RepoRoot -and (Test-Path (Join-Path $RepoRoot "src\Server\Jotdex.Server.csproj"))) {
        return (Resolve-Path $RepoRoot).Path
    }
    $here = $PSScriptRoot
    $candidate = Split-Path -Parent $here
    if (Test-Path (Join-Path $candidate "src\Server\Jotdex.Server.csproj")) {
        return $candidate
    }
    if (Test-Path (Join-Path (Get-Location) "src\Server\Jotdex.Server.csproj")) {
        return (Resolve-Path (Get-Location)).Path
    }
    return $null
}

# --- banner ---
Write-Host ""
Write-Host "  Jotdex guided setup (Windows)" -ForegroundColor White
Write-Host "  -----------------------------"
Write-Host "  This script will:"
Write-Host "    - Check for Git, .NET SDK 10+, and Node.js"
Write-Host "    - Offer to install missing tools with winget (optional, asks first)"
Write-Host "    - Create your notes vault folder"
Write-Host "    - Build the portable Jotdex app"
Write-Host "    - Optionally start it and add a Startup shortcut"
Write-Host ""
Write-Host "  It will NOT disable security software or change global PowerShell policy." -ForegroundColor DarkGray
Write-Host ""

if (-not $NonInteractive) {
    if (-not (Confirm-Yes "Continue?" $true)) {
        Write-Host "Cancelled."
        exit 0
    }
}

# --- tools ---
Write-Step "Checking required tools"

$needRefresh = $false

if (-not (Test-Command "git")) {
    Write-WarnLine "Git not found."
    if (-not $SkipDeps) {
        [void](Install-WithWinget "Git.Git" "Git for Windows")
        Refresh-Path
        $needRefresh = $true
    }
}
if (Test-Command "git") { Write-Ok "Git: $(git --version)" }
else { Write-WarnLine "Git still not on PATH. Install from https://git-scm.com/download/win then open a new PowerShell." }

$sdkMajor = Get-DotNetSdkMajor
if ($sdkMajor -lt 10) {
    if ($sdkMajor -gt 0) { Write-WarnLine ".NET SDK $sdkMajor found; Jotdex needs SDK 10+." }
    else { Write-WarnLine ".NET SDK not found." }
    if (-not $SkipDeps) {
        [void](Install-WithWinget "Microsoft.DotNet.SDK.10" ".NET 10 SDK")
        Refresh-Path
        $needRefresh = $true
        $sdkMajor = Get-DotNetSdkMajor
    }
}
if ($sdkMajor -ge 10) { Write-Ok ".NET SDK major version: $sdkMajor" }
else {
    Write-WarnLine ".NET 10 SDK still missing. Install from https://dotnet.microsoft.com/download then re-run."
}

if (-not (Test-Command "node")) {
    Write-WarnLine "Node.js not found."
    if (-not $SkipDeps) {
        [void](Install-WithWinget "OpenJS.NodeJS.LTS" "Node.js LTS")
        Refresh-Path
        $needRefresh = $true
    }
}
if (Test-Command "node") { Write-Ok "Node: $(node --version)" }
else { Write-WarnLine "Node still missing. Install LTS from https://nodejs.org/ then re-run." }

if ($needRefresh) { Refresh-Path }

$missing = @()
if (-not (Test-Command "git")) { $missing += "Git" }
if ((Get-DotNetSdkMajor) -lt 10) { $missing += ".NET 10 SDK" }
if (-not (Test-Command "node")) { $missing += "Node.js LTS" }
if ($missing.Count -gt 0 -and -not $SkipBuild) {
    Write-Host ""
    Write-Host "Cannot build yet. Missing: $($missing -join ', ')" -ForegroundColor Red
    Write-Host "Install those tools, close and reopen PowerShell, then run Setup again."
    Write-Host "Manual steps are also in README.md"
    exit 1
}

# --- repo ---
Write-Step "Locating Jotdex source"
$root = Resolve-RepoRoot
if (-not $root) {
    Write-Info "This folder is not a Jotdex checkout."
    $cloneParent = if ($NonInteractive) { Join-Path $HOME "Downloads" } else {
        $default = Join-Path $HOME "Downloads"
        $ans = Read-Host "Folder to download into [$default]"
        if ([string]::IsNullOrWhiteSpace($ans)) { $default } else { $ans }
    }
    New-Item -ItemType Directory -Force -Path $cloneParent | Out-Null
    $dest = Join-Path $cloneParent "jotdex"
    if (Test-Path $dest) {
        Write-Ok "Using existing $dest"
        $root = (Resolve-Path $dest).Path
    } else {
        if (-not (Test-Command "git")) { throw "Git is required to clone the repository." }
        Write-Info "Cloning https://github.com/jcline123/jotdex.git ..."
        git clone https://github.com/jcline123/jotdex.git $dest
        $root = (Resolve-Path $dest).Path
        Write-Ok "Cloned to $root"
    }
} else {
    Write-Ok "Repo: $root"
}

# --- vault ---
Write-Step "Notes vault (your Markdown folder)"
Write-Info "Live vault must be on local disk - not iCloud/OneDrive sync folders."
if (-not $VaultPath) {
    $defaultVault = "C:\JotdexVault"
    if ($NonInteractive) {
        $VaultPath = $defaultVault
    } else {
        $ans = Read-Host "Vault folder path [$defaultVault]"
        $VaultPath = if ([string]::IsNullOrWhiteSpace($ans)) { $defaultVault } else { $ans.Trim().Trim('"') }
    }
}

Assert-NotCloudSyncPath $VaultPath
if (-not (Test-Path $VaultPath)) {
    Write-Info "Creating $VaultPath"
    New-Item -ItemType Directory -Force -Path $VaultPath | Out-Null
} else {
    Write-Ok "Vault exists: $VaultPath"
}
$VaultPath = [System.IO.Path]::GetFullPath($VaultPath)
Write-Ok "Vault: $VaultPath"

# --- build ---
$artifacts = Join-Path $root "artifacts\win-x64"
$exe = Join-Path $artifacts "Jotdex.Server.exe"

if (-not $SkipBuild) {
    Write-Step "Building portable app (npm + publish). This can take several minutes."
    $publish = Join-Path $root "scripts\publish-win-x64.ps1"
    if (-not (Test-Path $publish)) { throw "Missing $publish" }
    & $publish
    if (-not (Test-Path $exe)) { throw "Build finished but $exe was not found." }
    Write-Ok "Built: $artifacts"
} else {
    if (-not (Test-Path $exe)) {
        throw "-SkipBuild set but $exe is missing. Run without -SkipBuild."
    }
    Write-Ok "Using existing build: $artifacts"
}

# Pre-seed vault path for portable data root
Write-Step "Writing vault setting for portable app"
$dataConfig = Join-Path $artifacts "data\config"
New-Item -ItemType Directory -Force -Path $dataConfig | Out-Null
$vaultJson = Join-Path $dataConfig "vault.json"
@{ vaultPath = $VaultPath } | ConvertTo-Json | Set-Content -Path $vaultJson -Encoding UTF8
Write-Ok "Wrote $vaultJson"

# Optional LAN + firewall
$wantLan = $false
if (-not $NonInteractive) {
    $wantLan = Confirm-Yes "Enable LAN access now (listen on all interfaces; prompts UAC for firewall)?" $false
}
if ($wantLan) {
    Write-Step "LAN network settings + Windows Firewall"
    $httpPort = 5180
    $networkJson = Join-Path $dataConfig "network.json"
    @{
        bindMode         = "lan"
        port             = $httpPort
        httpsSelfSigned  = $false
        httpsPort        = 0
        httpsPfxPath     = $null
        httpsPfxPassword = $null
    } | ConvertTo-Json | Set-Content -Path $networkJson -Encoding UTF8
    Write-Ok "Wrote $networkJson (bindMode=lan, port=$httpPort)"

    $fwScript = Join-Path $artifacts "Ensure-JotdexFirewall.ps1"
    if (-not (Test-Path -LiteralPath $fwScript)) {
        $fwScript = Join-Path $root "scripts\Ensure-JotdexFirewall.ps1"
    }
    $exePath = Join-Path $artifacts "Jotdex.Server.exe"
    if (Test-Path -LiteralPath $fwScript) {
        try {
            $argList = @(
                "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", $fwScript,
                "-HttpPort", "$httpPort",
                "-ProgramPath", $exePath
            )
            $p = Start-Process -FilePath "powershell.exe" -ArgumentList $argList -Verb RunAs -Wait -PassThru
            if ($p.ExitCode -eq 0) {
                Write-Ok "Firewall allow rules added for TCP $httpPort"
            } else {
                Write-WarnLine "Firewall helper exited $($p.ExitCode). LAN is still enabled — open TCP $httpPort manually if other PCs cannot connect."
            }
        } catch {
            Write-WarnLine "UAC/firewall skipped ($($_.Exception.Message)). LAN is still enabled — open TCP $httpPort in Windows Firewall if needed."
        }
    } else {
        Write-WarnLine "Ensure-JotdexFirewall.ps1 not found. Open TCP $httpPort in Windows Firewall if LAN clients cannot connect."
    }
}

# Startup shortcut
$wantStartup = [bool]$AddStartupShortcut
if (-not $NonInteractive -and -not $AddStartupShortcut) {
    $wantStartup = Confirm-Yes "Start Jotdex automatically when you sign in to Windows?" $true
}
if ($wantStartup) {
    Write-Step "Creating Startup shortcut"
    $startupDir = [Environment]::GetFolderPath("Startup")
    $cmdPath = Join-Path $startupDir "Jotdex Server.cmd"
    $workDir = $artifacts
    @"
@echo off
cd /d "$workDir"
start "" "$exe"
"@ | Set-Content -Path $cmdPath -Encoding ASCII
    Write-Ok "Startup: $cmdPath"
}

# Start?
$wantStart = [bool]$Start
if (-not $NonInteractive -and -not $Start) {
    $wantStart = Confirm-Yes "Start Jotdex now?" $true
}

Write-Host ""
Write-Host "  Setup complete" -ForegroundColor Green
Write-Host "  --------------"
Write-Host "  App folder : $artifacts"
Write-Host "  Vault      : $VaultPath"
Write-Host "  Start later: $artifacts\start-portable.cmd"
if ($wantLan) {
    Write-Host "  Browser    : http://<this-pc-ip>:5180  (LAN) or http://127.0.0.1:5180"
} else {
    Write-Host "  Browser    : http://127.0.0.1:5180"
}
Write-Host ""
Write-Host "  First open: finish any remaining wizard steps (admin password if asked)."
Write-Host "  Optional: Settings -> Network for LAN / HTTPS (UAC opens firewall); Settings -> Start with Windows."
Write-Host ""

if ($wantStart) {
    Write-Step "Starting Jotdex"
    Start-Process -FilePath (Join-Path $artifacts "start-portable.cmd") -WorkingDirectory $artifacts
    Start-Sleep -Seconds 2
    try {
        Start-Process "http://127.0.0.1:5180"
    } catch {
        Write-Info "Open http://127.0.0.1:5180 in your browser."
    }
}

exit 0
