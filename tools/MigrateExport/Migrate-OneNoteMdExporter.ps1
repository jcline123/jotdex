<#
.SYNOPSIS
  Migrate alxnbl/onenote-md-exporter output into a Jotdex vault.

.DESCRIPTION
  Does NOT modify the source export. Copies notes into a destination vault,
  moves referenced files from each notebook's shared resources/ folder into
  sibling NoteName.assets/ directories, and rewrites Markdown links.

.EXAMPLE
  .\Migrate-OneNoteMdExporter.ps1 `
    -SourceRoot "C:\Users\joshu\Downloads\OneNoteMdExporter.v1.6.0\Exports\md" `
    -Destination "C:\JotdexVault" `
    -DryRun
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [switch]$DryRun,

    [string]$ReportDir = ""
)

$ErrorActionPreference = "Stop"

function Get-Sha256([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::OpenRead($Path)
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($fs))).Replace("-", "").ToLowerInvariant()
        } finally { $fs.Dispose() }
    } finally { $sha.Dispose() }
}

function Sanitize-FileName([string]$name) {
    if ([string]::IsNullOrWhiteSpace($name)) { return "Untitled" }
    # Decode common HTML entities from exporter
    $n = [System.Net.WebUtility]::HtmlDecode($name)
    $n = $n -replace '[\u0000-\u001F]', ' '
    $invalid = [System.IO.Path]::GetInvalidFileNameChars()
    foreach ($ch in $invalid) { $n = $n.Replace([string]$ch, "-") }
    $n = $n.Trim().TrimEnd('.', ' ')
    if ([string]::IsNullOrWhiteSpace($n)) { $n = "Untitled" }
    # Reserved device names
    if ($n -match '^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$') { $n = "_$n" }
    if ($n.Length -gt 120) { $n = $n.Substring(0, 120).TrimEnd('.', ' ') }
    return $n
}

function Read-FrontMatter([string]$text) {
    $title = $null; $created = $null; $updated = $null; $body = $text
    if ($text.StartsWith("---")) {
        $end = $text.IndexOf("`n---", 3)
        if ($end -gt 0) {
            $fm = $text.Substring(3, $end - 3)
            $after = $end + 4
            if ($after -lt $text.Length -and $text[$after] -eq "`r") { $after++ }
            if ($after -lt $text.Length -and $text[$after] -eq "`n") { $after++ }
            $body = $text.Substring($after)
            foreach ($line in $fm -split "`r?`n") {
                if ($line -match '^\s*title:\s*(.+)\s*$') { $title = $Matches[1].Trim().Trim('"') }
                if ($line -match '^\s*created:\s*(.+)\s*$') { $created = $Matches[1].Trim() }
                if ($line -match '^\s*updated:\s*(.+)\s*$') { $updated = $Matches[1].Trim() }
            }
        }
    }
    return [pscustomobject]@{ Title = $title; Created = $created; Updated = $updated; Body = $body }
}

function Encode-MdPath([string]$rel) {
    ($rel -replace '\\', '/') -split '/' | ForEach-Object { [Uri]::EscapeDataString($_) } | Join-String -Separator '/'
}

# Join-String may not exist on older PS — polyfill
if (-not (Get-Command Join-String -ErrorAction SilentlyContinue)) {
    function Join-String {
        param([string]$Separator = "")
        begin { $items = @() }
        process { $items += $_ }
        end { return [string]::Join($Separator, $items) }
    }
}

if (-not (Test-Path -LiteralPath $SourceRoot)) { throw "SourceRoot not found: $SourceRoot" }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path

if (-not $ReportDir) {
    # $PSScriptRoot = <repo>/tools/MigrateExport → repo docs/import-format
    $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $ReportDir = Join-Path $repoRoot "docs\import-format"
}
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$reportJson = Join-Path $ReportDir "migration-report-$stamp.json"
$reportMd = Join-Path $ReportDir "migration-report-$stamp.md"

Write-Host "Source: $SourceRoot"
Write-Host "Destination: $Destination"
Write-Host "DryRun: $DryRun"

# Discover notebook export folders: each has resources/ + one content folder
$notebooks = @()
Get-ChildItem -LiteralPath $SourceRoot -Directory | ForEach-Object {
    $res = Join-Path $_.FullName "resources"
    if (-not (Test-Path -LiteralPath $res)) { return }
    $contentDirs = Get-ChildItem -LiteralPath $_.FullName -Directory | Where-Object { $_.Name -ne "resources" }
    $mdCount = 0
    foreach ($cd in $contentDirs) {
        $mdCount += @(Get-ChildItem -LiteralPath $cd.FullName -Recurse -Filter *.md -ErrorAction SilentlyContinue).Count
    }
    if ($mdCount -eq 0) {
        Write-Host "Skipping empty export folder: $($_.Name)"
        return
    }
    foreach ($cd in $contentDirs) {
        $notebooks += [pscustomobject]@{
            ExportFolder = $_.FullName
            ResourcesDir = $res
            ContentRoot  = $cd.FullName
            NotebookName = Sanitize-FileName $cd.Name
            MdCount      = @(Get-ChildItem -LiteralPath $cd.FullName -Recurse -Filter *.md).Count
        }
    }
}

if ($notebooks.Count -eq 0) { throw "No alxnbl notebook folders found under $SourceRoot" }

Write-Host "Notebooks:"
$notebooks | ForEach-Object { Write-Host ("  - {0} ({1} notes) from {2}" -f $_.NotebookName, $_.MdCount, $_.ExportFolder) }

if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $marker = Join-Path $Destination ".notes-vault.json"
    if (-not (Test-Path -LiteralPath $marker)) {
        $vaultId = [guid]::NewGuid().ToString("D")
        @{
            id            = $vaultId
            formatVersion = 1
            name          = "Jotdex Vault"
            created       = (Get-Date).ToUniversalTime().ToString("o")
            source        = "onenote-md-exporter"
        } | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
    }
}

$notesImported = 0
$imagesCopied = 0
$attachmentsCopied = 0
$missingResources = New-Object System.Collections.Generic.List[string]
$duplicates = New-Object System.Collections.Generic.List[string]
$noteRecords = New-Object System.Collections.Generic.List[object]
$resourceRefRegex = [regex]'(!?\[[^\]]*\]\()((?:\.\./)+resources/([^)\s]+))(\))'

foreach ($nb in $notebooks) {
    $nbDest = Join-Path $Destination $nb.NotebookName
    Write-Host "`nMigrating notebook '$($nb.NotebookName)' ..."

    $mdFiles = Get-ChildItem -LiteralPath $nb.ContentRoot -Recurse -Filter *.md
    foreach ($md in $mdFiles) {
        $relFromContent = $md.FullName.Substring($nb.ContentRoot.Length).TrimStart('\', '/')
        $sectionRel = Split-Path $relFromContent -Parent
        if ($sectionRel -eq ".") { $sectionRel = "" }

        $raw = [System.IO.File]::ReadAllText($md.FullName)
        $fm = Read-FrontMatter $raw
        $title = if ($fm.Title) { [System.Net.WebUtility]::HtmlDecode($fm.Title) } else { [IO.Path]::GetFileNameWithoutExtension($md.Name) }
        $title = [System.Net.WebUtility]::HtmlDecode($title)
        $stem = Sanitize-FileName $title

        $destSection = if ($sectionRel) {
            $parts = $sectionRel -split '[\\/]' | ForEach-Object { Sanitize-FileName $_ }
            Join-Path $nbDest ($parts -join [IO.Path]::DirectorySeparatorChar)
        } else { $nbDest }

        $destMd = Join-Path $destSection ($stem + ".md")
        $n = 1
        while ((Test-Path -LiteralPath $destMd) -or ($noteRecords | Where-Object { $_.DestPath -eq $destMd })) {
            $suffix = " ($n)"
            $destMd = Join-Path $destSection ($stem + $suffix + ".md")
            $n++
            if ($n -eq 2) { $duplicates.Add("$($nb.NotebookName)/$sectionRel/$stem") | Out-Null }
        }
        $finalStem = [IO.Path]::GetFileNameWithoutExtension($destMd)
        $assetsDir = Join-Path $destSection ($finalStem + ".assets")

        # Collect and rewrite resource links
        $body = $fm.Body
        $copiedNames = @{}
        $newBody = $resourceRefRegex.Replace($body, {
            param($m)
            $fileName = $m.Groups[3].Value
            $fileName = [Uri]::UnescapeDataString($fileName)
            $srcFile = Join-Path $nb.ResourcesDir $fileName
            if (-not (Test-Path -LiteralPath $srcFile)) {
                $missingResources.Add("$($md.FullName) -> resources/$fileName") | Out-Null
                return $m.Value
            }
            if (-not $copiedNames.ContainsKey($fileName)) {
                $copiedNames[$fileName] = $true
                if (-not $DryRun) {
                    New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
                    $destFile = Join-Path $assetsDir $fileName
                    if (-not (Test-Path -LiteralPath $destFile)) {
                        Copy-Item -LiteralPath $srcFile -Destination $destFile -Force
                    }
                }
                $ext = [IO.Path]::GetExtension($fileName).ToLowerInvariant()
                if ($ext -in ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg") { $script:imagesCopied++ }
                else { $script:attachmentsCopied++ }
            }
            $linkPath = (Encode-MdPath ($finalStem + ".assets/" + $fileName))
            return $m.Groups[1].Value + $linkPath + $m.Groups[4].Value
        })

        $id = [guid]::NewGuid().ToString("D")
        $created = if ($fm.Created) { $fm.Created } else { $md.CreationTimeUtc.ToString("o") }
        $modified = if ($fm.Updated) { $fm.Updated } else { $md.LastWriteTimeUtc.ToString("o") }
        # Normalize created/updated to ISO-ish if missing Z
        if ($created -notmatch 'Z$|[\+\-]\d{2}:\d{2}$') { $created = $created + "Z" }
        if ($modified -notmatch 'Z$|[\+\-]\d{2}:\d{2}$') { $modified = $modified + "Z" }

        $yamlTitle = $title -replace '"', '\"'
        $front = @(
            "---"
            "id: $id"
            "title: `"$yamlTitle`""
            "created: $created"
            "modified: $modified"
            "tags: []"
            "source: onenote"
            "onenote_export: alxnbl/onenote-md-exporter"
            "---"
            ""
        ) -join "`n"

        $outText = $front + $newBody.TrimStart()
        if (-not $outText.EndsWith("`n")) { $outText += "`n" }

        $hash = $null
        if (-not $DryRun) {
            New-Item -ItemType Directory -Force -Path $destSection | Out-Null
            $utf8NoBom = New-Object System.Text.UTF8Encoding $false
            [System.IO.File]::WriteAllText($destMd, $outText, $utf8NoBom)
            $hash = Get-Sha256 $destMd
        }

        $notesImported++
        $noteRecords.Add([pscustomobject]@{
            Notebook   = $nb.NotebookName
            SourcePath = $md.FullName
            DestPath   = $destMd
            Title      = $title
            Assets     = $copiedNames.Count
            Id         = $id
            Hash       = $hash
        }) | Out-Null
    }
}

$report = [pscustomobject]@{
    sourceRoot          = $SourceRoot
    destination         = $Destination
    dryRun              = [bool]$DryRun
    startedUtc          = (Get-Date).ToUniversalTime().ToString("o")
    notebooks           = @($notebooks | ForEach-Object { @{ name = $_.NotebookName; notes = $_.MdCount; exportFolder = $_.ExportFolder } })
    notesImported       = $notesImported
    imagesCopied        = $imagesCopied
    attachmentsCopied   = $attachmentsCopied
    missingResources    = @($missingResources)
    duplicateTitles     = @($duplicates)
    notes               = @($noteRecords | Select-Object Notebook, Title, SourcePath, DestPath, Assets, Id, Hash)
}

$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportJson -Encoding UTF8

$mdReport = @"
# Migration report ($stamp)

- **Source:** ``$SourceRoot``
- **Destination:** ``$Destination``
- **Dry run:** $DryRun
- **Notes imported:** $notesImported
- **Images copied:** $imagesCopied
- **Other attachments copied:** $attachmentsCopied
- **Missing resources:** $($missingResources.Count)
- **Duplicate titles resolved:** $($duplicates.Count)

## Notebooks

$($notebooks | ForEach-Object { "- **$($_.NotebookName):** $($_.MdCount) notes" } | Join-String -Separator "`n")

## Missing resources (first 50)

$(if ($missingResources.Count -eq 0) { "_None_" } else { ($missingResources | Select-Object -First 50 | ForEach-Object { "- $_" }) -join "`n" })

## Tool

``tools/MigrateExport/Migrate-OneNoteMdExporter.ps1`` — for [alxnbl/onenote-md-exporter](https://github.com/alxnbl/onenote-md-exporter) layout (``resources/`` + notebook folder).

Original export was **not** modified.
"@
$mdReport | Set-Content -LiteralPath $reportMd -Encoding UTF8

Write-Host "`nDone. Notes=$notesImported Images=$imagesCopied Attachments=$attachmentsCopied Missing=$($missingResources.Count)"
Write-Host "Report: $reportMd"
Write-Host "JSON:   $reportJson"
