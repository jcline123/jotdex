# Isolated Playwright run: temp SampleVault + temp data root. Never uses C:\JotdexVault.
param(
  [int]$Port = 5191
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tmp = Join-Path $env:TEMP ("jotdex-e2e-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$vault = Join-Path $tmp 'vault'
$data = Join-Path $tmp 'data'
New-Item -ItemType Directory -Force -Path $vault, $data | Out-Null
Copy-Item (Join-Path $root 'tools\SampleVault\*') $vault -Recurse -Force

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Jotdex__VaultPath = $vault
$env:Jotdex__DataRoot = $data
$env:Jotdex__PortableMode = 'true'
$env:JOTDEX_E2E_ISOLATED = '1'
$env:JOTDEX_E2E_BASE = "http://127.0.0.1:$Port"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"

$server = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--no-launch-profile', '--project', (Join-Path $root 'src\Server\Jotdex.Server.csproj')) -PassThru -WindowStyle Hidden
try {
  $ok = $false
  foreach ($i in 1..40) {
    try {
      $r = Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$Port/api/health" -TimeoutSec 2
      if ($r.StatusCode -eq 200) { $ok = $true; break }
    } catch { Start-Sleep -Milliseconds 500 }
  }
  if (-not $ok) { throw "Isolated server did not start on $Port" }
  Push-Location (Join-Path $root 'src\Web')
  try { npx playwright test }
  finally { Pop-Location }
} finally {
  if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}
