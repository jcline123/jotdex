# Installs PSScriptAnalyzer into src/PowerShellDiagnostics/modules for portable builds.
# Safe to re-run. Requires PowerShell Gallery access once.
# Non-interactive: bootstraps NuGet provider when missing.

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $repo "src\PowerShellDiagnostics\modules"
New-Item -ItemType Directory -Path $dest -Force | Out-Null

if (-not (Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue)) {
    Write-Host "Bootstrapping NuGet provider..."
    Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force | Out-Null
}

Write-Host "Installing PSScriptAnalyzer to $dest ..."
Save-Module -Name PSScriptAnalyzer -Path $dest -Force
Write-Host "Done. Module path: $(Join-Path $dest 'PSScriptAnalyzer')"
