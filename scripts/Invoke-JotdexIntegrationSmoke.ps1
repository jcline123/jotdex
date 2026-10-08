#Requires -Version 5.1
<#
.SYNOPSIS
  Smoke-test Jotdex Integrations API (read + optional create in a test folder).

.DESCRIPTION
  Reads secrets from environment only — never hardcode tokens.
  Required: JOTDEX_BASE_URL, JOTDEX_API_TOKEN
  Optional: CF_ACCESS_CLIENT_ID, CF_ACCESS_CLIENT_SECRET
  Optional write: JOTDEX_SMOKE_FOLDER (default Inbox/IntegrationSmoke) — create/append only if set and token allows.

.NOTES
  TLS certificate validation is left on (do not disable).
#>
param(
    [string]$BaseUrl = $env:JOTDEX_BASE_URL,
    [string]$Token = $env:JOTDEX_API_TOKEN,
    [string]$SmokeFolder = $(if ($env:JOTDEX_SMOKE_FOLDER) { $env:JOTDEX_SMOKE_FOLDER } else { "Inbox/IntegrationSmoke" }),
    [switch]$Write
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($Token)) {
    throw "Set JOTDEX_BASE_URL and JOTDEX_API_TOKEN environment variables."
}

$BaseUrl = $BaseUrl.TrimEnd("/")
$headers = @{
    Authorization = "Bearer $Token"
    Accept        = "application/json"
}
if ($env:CF_ACCESS_CLIENT_ID -and $env:CF_ACCESS_CLIENT_SECRET) {
    $headers["CF-Access-Client-Id"] = $env:CF_ACCESS_CLIENT_ID
    $headers["CF-Access-Client-Secret"] = $env:CF_ACCESS_CLIENT_SECRET
}

function Invoke-Jdx([string]$Method, [string]$Path, [hashtable]$ExtraHeaders = @{}, $Body = $null) {
    $h = @{} + $headers + $ExtraHeaders
    $uri = "$BaseUrl$Path"
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $h
    }
    $json = $Body | ConvertTo-Json -Depth 6 -Compress
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $h -ContentType "application/json" -Body $json
}

Write-Host "GET whoami..."
$me = Invoke-Jdx GET "/api/integrations/v1/whoami"
Write-Host ("  token={0} scopes={1}" -f $me.name, ($me.permissions -join ","))

Write-Host "GET folders..."
$folders = Invoke-Jdx GET "/api/integrations/v1/folders"
Write-Host ("  count={0}" -f @($folders.folders).Count)

Write-Host "GET notes (limit 5)..."
$notes = Invoke-Jdx GET "/api/integrations/v1/notes?limit=5"
Write-Host ("  count={0}" -f @($notes.notes).Count)

Write-Host "GET search..."
$hits = Invoke-Jdx GET ("/api/integrations/v1/search?q={0}&limit=5" -f [uri]::EscapeDataString("test"))
Write-Host ("  hits={0}" -f @($hits.hits).Count)

if (-not $Write) {
    Write-Host "Read smoke OK (pass -Write to create/append under $SmokeFolder)."
    exit 0
}

$idKey = [guid]::NewGuid().ToString("N")
$title = "Integration smoke " + (Get-Date -Format "yyyyMMdd-HHmmss")
Write-Host "POST create in $SmokeFolder (Idempotency-Key=$idKey)..."
$created = Invoke-Jdx POST "/api/integrations/v1/notes" @{
    "Idempotency-Key" = $idKey
} @{
    title         = $title
    folderPath    = $SmokeFolder
    bodyMarkdown  = "Smoke body from Invoke-JotdexIntegrationSmoke.ps1`n"
}
Write-Host ("  id={0} etag={1}" -f $created.id, $created.etag)

$appendKey = [guid]::NewGuid().ToString("N")
Write-Host "POST append..."
$appended = Invoke-Jdx POST "/api/integrations/v1/notes/$($created.id)/append" @{
    "Idempotency-Key" = $appendKey
    "If-Match"        = $created.etag
} @{
    bodyMarkdown = "`n## Append`nMore text.`n"
}
Write-Host ("  etag={0}" -f $appended.etag)
Write-Host "Write smoke OK."
