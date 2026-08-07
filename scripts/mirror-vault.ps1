# Mirror a local Jotdex vault to a read-only destination (e.g. iCloud).
# Do NOT set Jotdex VaultPath to the destination.

param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Source vault folder not found: $Source"
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$args = @(
    $Source,
    $Destination,
    "/MIR",
    "/R:2",
    "/W:2",
    "/NFL",
    "/NDL",
    "/NP",
    "/XD", ".git"
)

if ($WhatIf) {
    Write-Host "Would run: robocopy $($args -join ' ')"
    exit 0
}

Write-Host "Mirroring vault:"
Write-Host "  from $Source"
Write-Host "  to   $Destination"
$p = Start-Process -FilePath "robocopy.exe" -ArgumentList $args -Wait -PassThru -NoNewWindow
# robocopy exit codes 0-7 are success/partial
if ($p.ExitCode -ge 8) {
    throw "robocopy failed with exit code $($p.ExitCode)"
}

Write-Host "Mirror complete (robocopy exit $($p.ExitCode))."
Write-Host "Remember: keep Jotdex VaultPath on local disk, not the mirror."
