# Mirror a local Jotdex vault to a read-only destination as a single ZIP
# (friendlier for iCloud / OneDrive on Windows than thousands of small files).
# Do NOT set Jotdex VaultPath to the destination.

param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [string]$ZipName = "jotdex-vault-mirror.zip",

    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Source vault folder not found: $Source"
}

$srcFull = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\', '/')
$destFull = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\', '/')
if ($srcFull -ieq $destFull) {
    throw "Destination cannot be the live vault folder."
}
if ($destFull.StartsWith($srcFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Destination cannot be inside the live vault."
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$zipPath = Join-Path $Destination $ZipName
$tmpZip = Join-Path $Destination ($ZipName + "." + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".tmp")

if ($WhatIf) {
    Write-Host "Would zip: $srcFull -> $zipPath"
    exit 0
}

Write-Host "Zipping vault mirror:"
Write-Host "  from $srcFull"
Write-Host "  to   $zipPath"

if (Test-Path -LiteralPath $tmpZip) { Remove-Item -LiteralPath $tmpZip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $srcFull,
    $tmpZip,
    [System.IO.Compression.CompressionLevel]::Fastest,
    $false
)
Move-Item -LiteralPath $tmpZip -Destination $zipPath -Force

$keep = @($ZipName, "jotdex-move-kits", "README-JOTDEX-MIRROR.txt")
Get-ChildItem -LiteralPath $Destination -Force | ForEach-Object {
    if ($keep -contains $_.Name) { return }
    Write-Host "Removing legacy mirror item: $($_.Name)"
    Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Continue
}

$readme = Join-Path $Destination "README-JOTDEX-MIRROR.txt"
@"
Jotdex vault mirror (read-only zip)
==================================

This folder holds jotdex-vault-mirror.zip — a one-way copy of the live vault.
Do NOT open this folder as the live Jotdex vault. Keep VaultPath on local disk.
"@ | Set-Content -LiteralPath $readme -Encoding UTF8

Write-Host "Mirror zip complete."
Write-Host "Remember: keep Jotdex VaultPath on local disk, not the mirror."
