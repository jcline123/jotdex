#Requires -Version 5.1
<#
.SYNOPSIS
  Add or remove Windows Firewall inbound rules for Jotdex LAN access (HTTP + HTTPS).

.DESCRIPTION
  Intended to run elevated (UAC). Safe to re-run; updates existing Jotdex rules.

.PARAMETER HttpPort
  HTTP listen port (default 5180).

.PARAMETER HttpsPort
  HTTPS listen port (omit or 0 to skip HTTPS rule).

.PARAMETER Enable
  Create/update allow rules (default).

.PARAMETER Disable
  Remove Jotdex LAN firewall rules.

.PARAMETER ProgramPath
  Optional path to Jotdex.Server.exe (adds program-scoped rules when present).
#>
[CmdletBinding(DefaultParameterSetName = "Enable")]
param(
    [Parameter(ParameterSetName = "Enable")]
    [int]$HttpPort = 5180,

    [Parameter(ParameterSetName = "Enable")]
    [int]$HttpsPort = 0,

    [Parameter(ParameterSetName = "Enable")]
    [switch]$Enable,

    [Parameter(ParameterSetName = "Disable")]
    [switch]$Disable,

    [string]$ProgramPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RuleHttp = "Jotdex LAN HTTP"
$RuleHttps = "Jotdex LAN HTTPS"

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Remove-JotdexRules {
    foreach ($name in @($RuleHttp, $RuleHttps)) {
        Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    }
}

if (-not (Test-IsAdmin)) {
    Write-Error "Administrator elevation is required to change Windows Firewall rules."
    exit 2
}

if ($Disable -or $PSCmdlet.ParameterSetName -eq "Disable") {
    Remove-JotdexRules
    Write-Host "Removed Jotdex LAN firewall rules (if any)."
    exit 0
}

if ($HttpPort -lt 1 -or $HttpPort -gt 65535) { throw "HttpPort out of range." }
if ($HttpsPort -ne 0 -and ($HttpsPort -lt 1 -or $HttpsPort -gt 65535)) { throw "HttpsPort out of range." }
if ($HttpsPort -ne 0 -and $HttpsPort -eq $HttpPort) { throw "HttpsPort must differ from HttpPort." }

Remove-JotdexRules

$common = @{
    Direction          = "Inbound"
    Action             = "Allow"
    Protocol           = "TCP"
    Profile            = "Any"
    Enabled            = "True"
}

if ($ProgramPath -and (Test-Path -LiteralPath $ProgramPath)) {
    $exe = (Resolve-Path -LiteralPath $ProgramPath).Path
    New-NetFirewallRule @common -DisplayName $RuleHttp -Name "Jotdex.Lan.Http" `
        -Description "Allow inbound HTTP to Jotdex on LAN" `
        -LocalPort $HttpPort -Program $exe | Out-Null
    Write-Host "OK HTTP port $HttpPort (program $exe)"
    if ($HttpsPort -gt 0) {
        New-NetFirewallRule @common -DisplayName $RuleHttps -Name "Jotdex.Lan.Https" `
            -Description "Allow inbound HTTPS to Jotdex on LAN" `
            -LocalPort $HttpsPort -Program $exe | Out-Null
        Write-Host "OK HTTPS port $HttpsPort (program $exe)"
    }
} else {
    New-NetFirewallRule @common -DisplayName $RuleHttp -Name "Jotdex.Lan.Http" `
        -Description "Allow inbound HTTP to Jotdex on LAN" `
        -LocalPort $HttpPort | Out-Null
    Write-Host "OK HTTP port $HttpPort"
    if ($HttpsPort -gt 0) {
        New-NetFirewallRule @common -DisplayName $RuleHttps -Name "Jotdex.Lan.Https" `
            -Description "Allow inbound HTTPS to Jotdex on LAN" `
            -LocalPort $HttpsPort | Out-Null
        Write-Host "OK HTTPS port $HttpsPort"
    }
}

exit 0
