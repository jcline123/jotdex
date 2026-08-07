# Install Jotdex as a Windows Service (run elevated).
# Expects to live beside Jotdex.Server.exe (portable publish output) or pass -ExePath.
# Starts Automatically after reboot. Prefer data/config/network.json for bind/port (do not force --urls).

param(
    [string]$ServiceName = "Jotdex",
    [string]$DisplayName = "Jotdex Markdown Notes Server",
    [string]$ExePath = ""
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

# No --urls: NetworkListenConfigurator reads network.json (LAN/HTTPS settings survive reboot).
$binPath = "`"$exeFull`""
Write-Host "Creating service $ServiceName (Automatic start)"
New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName $DisplayName -StartupType Automatic -Description "Self-hosted Markdown notes server (Jotdex)" | Out-Null

# Ensure the service starts in the publish folder so portable .\data works
sc.exe config $ServiceName binPath= $binPath start= auto | Out-Null
# AppDirectory via registry for working directory
$reg = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $reg -Name AppDirectory -Value $workDir -PropertyType String -Force | Out-Null

Write-Host "Starting $ServiceName..."
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Format-List Name, Status, StartType
Write-Host "Working directory: $workDir"
Write-Host "App data: $workDir\data (PortableMode) or %LOCALAPPDATA%\Jotdex"
Write-Host "Logs: (app data)\logs\jotdex-YYYYMMDD.log"
Write-Host "Open the listen URL from Settings → Network (default http://127.0.0.1:5180)."
