# Install Jotdex as a Windows Service (run elevated).
# Expects to live beside Jotdex.Server.exe (portable publish output) or pass -ExePath.

param(
    [string]$ServiceName = "Jotdex",
    [string]$DisplayName = "Jotdex Markdown Notes Server",
    [string]$ExePath = "",
    [string]$Urls = "http://127.0.0.1:5180"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}

if (-not $ExePath) {
    $ExePath = Join-Path $PSScriptRoot "Jotdex.Server.exe"
}
if (-not (Test-Path $ExePath)) {
    throw "Executable not found: $ExePath"
}

$exeFull = (Resolve-Path $ExePath).Path
$workDir = Split-Path -Parent $exeFull

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service $ServiceName..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Use LocalSystem for MVP; harden to a dedicated account later.
$binPath = "`"$exeFull`" --urls $Urls"
Write-Host "Creating service $ServiceName"
New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName $DisplayName -StartupType Automatic -Description "Self-hosted Markdown notes server (Jotdex)" | Out-Null

# Working directory for portable .\data — set via registry AppDirectory if needed.
# Kestrel inherits the service's working directory from sc; set it explicitly:
sc.exe config $ServiceName start= auto | Out-Null

Write-Host "Starting $ServiceName..."
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Format-List Name, Status, StartType
Write-Host "App data: $workDir\data (PortableMode) or %LOCALAPPDATA%\Jotdex"
Write-Host "Open $Urls — complete first-run setup if prompted."
