#Requires -Version 5.1
<#
.SYNOPSIS
  Decrypt a Jotdex .jotdexkit move kit using your Jotdex unlock password.

.DESCRIPTION
  Calls Jotdex.Server.exe --decrypt-kit (works on Windows PowerShell 5.1).
  That CLI supports both kit formats: legacy JDXK1 and streaming JDXK2.
  Prefer Restore-Jotdex.ps1 when possible — it decrypts and restores in one step.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$KitPath,
    [string]$Password = "",
    [string]$OutputZip = "",
    [string]$ServerExe = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$KitPath = [System.IO.Path]::GetFullPath($KitPath)
if (-not (Test-Path -LiteralPath $KitPath)) { throw "File not found: $KitPath" }

if ([string]::IsNullOrWhiteSpace($Password)) {
    $secure = Read-Host "Jotdex unlock password" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $OutputZip = [System.IO.Path]::ChangeExtension($KitPath, ".zip")
}

function Find-ServerExe([string]$Hint) {
    if ($Hint -and (Test-Path -LiteralPath $Hint)) { return (Resolve-Path -LiteralPath $Hint).Path }
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    foreach ($c in @(
            (Join-Path $scriptDir "Jotdex.Server.exe"),
            (Join-Path (Get-Location) "Jotdex.Server.exe")
        )) {
        if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path }
    }
    return $null
}

$exe = Find-ServerExe $ServerExe
if (-not $exe) {
    throw "Jotdex.Server.exe not found. Place Decrypt-JotdexKit.ps1 next to the portable exe (or pass -ServerExe)."
}

$env:JOTDEX_DECRYPT_PASSWORD = $Password
try {
    & $exe --decrypt-kit $KitPath $OutputZip
    if ($LASTEXITCODE -ne 0) { throw "Decrypt failed (exit $LASTEXITCODE)." }
} finally {
    Remove-Item Env:\JOTDEX_DECRYPT_PASSWORD -ErrorAction SilentlyContinue
}

Write-Host "Decrypted OK: $OutputZip" -ForegroundColor Green
Write-Host "Next: unzip and run Restore-Jotdex.ps1"
