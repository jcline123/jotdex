# Publish a self-contained win-x64 portable build of Jotdex.
# Requires: .NET 10 SDK, and a prior `npm run build` in src/Web (copies to Server/wwwroot).

param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repo "artifacts\win-x64"
}

$web = Join-Path $repo "src\Web"
$wwwroot = Join-Path $repo "src\Server\wwwroot\index.html"
if (-not (Test-Path $wwwroot)) {
    Write-Host "Building SPA (wwwroot missing)..."
    Push-Location $web
    try {
        npm install
        npm run build
    } finally {
        Pop-Location
    }
}

if (Test-Path $OutputRoot) {
    Remove-Item $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null

$serverProj = Join-Path $repo "src\Server\Jotdex.Server.csproj"
Write-Host "Publishing $serverProj -> $OutputRoot"
dotnet publish $serverProj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $OutputRoot

# Portable helpers
Copy-Item (Join-Path $PSScriptRoot "start-portable.cmd") $OutputRoot -Force
Copy-Item (Join-Path $PSScriptRoot "install-service.ps1") $OutputRoot -Force
Copy-Item (Join-Path $PSScriptRoot "uninstall-service.ps1") $OutputRoot -Force
Copy-Item (Join-Path $PSScriptRoot "Restore-Jotdex.ps1") $OutputRoot -Force
Copy-Item (Join-Path $PSScriptRoot "Update-Jotdex.ps1") $OutputRoot -Force
Copy-Item (Join-Path $PSScriptRoot "Decrypt-JotdexKit.ps1") $OutputRoot -Force
Copy-Item (Join-Path $PSScriptRoot "Ensure-JotdexFirewall.ps1") $OutputRoot -Force

$psaModule = Join-Path $repo "src\PowerShellDiagnostics\modules\PSScriptAnalyzer"
if (-not (Test-Path $psaModule)) {
    Write-Host "PSScriptAnalyzer module missing - installing for portable bundle..."
    & (Join-Path $PSScriptRoot "install-psscriptanalyzer.ps1")
}
if (Test-Path $psaModule) {
    $outModules = Join-Path $OutputRoot "modules"
    New-Item -ItemType Directory -Path $outModules -Force | Out-Null
    Copy-Item $psaModule (Join-Path $outModules "PSScriptAnalyzer") -Recurse -Force
    Write-Host "Bundled PSScriptAnalyzer -> $outModules"
} else {
    Write-Warning "PSScriptAnalyzer not installed; PowerShell best-practice hints will be syntax-only in this build."
}

$example = Join-Path $OutputRoot "appsettings.example.json"
$exampleJson = @'
{
  "Jotdex": {
    "VaultPath": "",
    "DataRoot": "",
    "PortableMode": true,
    "Auth": {
      "IdleTimeoutMinutes": 60,
      "BypassInDevelopment": false
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
'@
Set-Content -Path $example -Value $exampleJson -Encoding UTF8

$readme = Join-Path $OutputRoot "README-PORTABLE.txt"
$readmeText = @'
Jotdex portable build
=====================

1. Prefer a local-disk vault folder (not iCloud), e.g. C:\JotdexVault
2. Double-click start-portable.cmd  (binds http://127.0.0.1:5180 by default)
3. Open the URL in a browser
4. First-run: choose vault path, optional password (no username), network bind/port
5. App data is stored in .\data beside this exe (password hash under data\auth if set)

LAN access: use Settings -> Network (LAN + port), then Save (UAC may prompt for firewall) and Restart.
Or pass: Jotdex.Server.exe --urls http://0.0.0.0:5180
Manual firewall: run Ensure-JotdexFirewall.ps1 as Administrator from this folder.

Windows Service: run install-service.ps1 elevated from this folder.
'@
Set-Content -Path $readme -Value $readmeText -Encoding UTF8

Write-Host "Done: $OutputRoot"
Get-ChildItem $OutputRoot | Select-Object Name, Length

# Convenience zip for GitHub Releases (upload this asset as jotdex-win-x64.zip)
$releaseZip = Join-Path (Split-Path $OutputRoot -Parent) "jotdex-win-x64.zip"
if (Test-Path -LiteralPath $releaseZip) { Remove-Item -LiteralPath $releaseZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stageZip = Join-Path $env:TEMP ("jotdex-release-zip-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stageZip | Out-Null
try {
    Get-ChildItem -LiteralPath $OutputRoot -Force | Where-Object { $_.Name -ne "data" } | ForEach-Object {
        $dest = Join-Path $stageZip $_.Name
        if ($_.PSIsContainer) {
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
        }
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stageZip, $releaseZip)
    Write-Host "Release zip: $releaseZip"
} finally {
    Remove-Item -LiteralPath $stageZip -Recurse -Force -ErrorAction SilentlyContinue
}
