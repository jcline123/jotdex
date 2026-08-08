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

# If LAN is already configured, ensure firewall (we are elevated).
$netJson = Join-Path $workDir "data\config\network.json"
$fwScript = Join-Path $workDir "Ensure-JotdexFirewall.ps1"
if ((Test-Path -LiteralPath $netJson) -and (Test-Path -LiteralPath $fwScript)) {
    try {
        $net = Get-Content -LiteralPath $netJson -Raw | ConvertFrom-Json
        if ([string]$net.bindMode -eq "lan") {
            $httpPort = 5180
            if ($net.port) { $httpPort = [int]$net.port }
            $httpsPort = 0
            $httpsOn = ($net.httpsSelfSigned -eq $true) -or (-not [string]::IsNullOrWhiteSpace([string]$net.httpsPfxPath))
            if ($httpsOn) {
                if ($net.httpsPort -and [int]$net.httpsPort -gt 0) { $httpsPort = [int]$net.httpsPort }
                else { $httpsPort = $httpPort + 1 }
            }
            Write-Host "LAN bind detected — ensuring firewall rules..."
            $fwArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $fwScript, "-HttpPort", "$httpPort", "-ProgramPath", $exeFull)
            if ($httpsPort -gt 0) { $fwArgs += @("-HttpsPort", "$httpsPort") }
            & powershell.exe @fwArgs
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Firewall helper exited $LASTEXITCODE. Open the HTTP/HTTPS ports manually if LAN clients cannot connect."
            }
        }
    } catch {
        Write-Warning "Could not ensure firewall rules: $($_.Exception.Message)"
    }
}

Write-Host "Starting $ServiceName..."
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Format-List Name, Status, StartType
Write-Host "Working directory: $workDir"
Write-Host "App data: $workDir\data (PortableMode) or %LOCALAPPDATA%\Jotdex"
Write-Host "Logs: (app data)\logs\jotdex-YYYYMMDD.log"
Write-Host "Open the listen URL from Settings → Network (default http://127.0.0.1:5180)."
