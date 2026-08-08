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

$example = Join-Path $OutputRoot "appsettings.example.json"
@"
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
"@ | Set-Content -Path $example -Encoding UTF8

$readme = Join-Path $OutputRoot "README-PORTABLE.txt"
@"
Jotdex portable build
=====================

1. Prefer a local-disk vault folder (not iCloud), e.g. C:\JotdexVault
2. Double-click start-portable.cmd  (binds http://127.0.0.1:5180 by default)
3. Open the URL in a browser
4. First-run: choose vault path, optional password (no username), network bind/port
5. App data is stored in .\data beside this exe (password hash under data\auth if set)

LAN access: use Settings → Network (LAN + port), then restart.
Or pass: Jotdex.Server.exe --urls http://0.0.0.0:5180

Windows Service: run install-service.ps1 elevated from this folder.
"@ | Set-Content -Path $readme -Encoding UTF8

Write-Host "Done: $OutputRoot"
Get-ChildItem $OutputRoot | Select-Object Name, Length
