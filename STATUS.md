# Jotdex STATUS

**Active milestone:** CE complete (CE-00–07)  
**Last updated:** 2026-08-22

## In progress

- (none)

## Just shipped (this session)

- Portable **1.1.19**: code editor milestone (Edit dialog, snippets, formatting check, PSScriptAnalyzer optional)
- Code boxes: basic TipTap edit; CodeMirror only via **Edit**

## Run (Development + SampleVault)

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
cd src\Server
dotnet run --no-launch-profile
```

Listen URL comes from `data/config/network.json` when present (often `http://127.0.0.1:5180`).

Optional PSScriptAnalyzer (also auto-installed by `publish-win-x64.ps1`):

```powershell
.\scripts\install-psscriptanalyzer.ps1
```

## Durable memory

- Fix / behavior rationale: [`docs/changelog.md`](docs/changelog.md)
- Architecture decisions: [`docs/decisions/`](docs/decisions/)
- Code editor ADR: [`docs/decisions/ADR-code-editor-diagnostics.md`](docs/decisions/ADR-code-editor-diagnostics.md)

## Known small polish (not blocking)

- Search box can show `undefined` as the value
- Leftover `tools/SampleVault/Conflict *.md` from earlier conflict tests (safe to delete)
- NuGet NU1903 warning on SQLitePCLRaw (dependency bump later)
- Cloud backup Dropbox/Google live matrices pending (`docs/cloud-backup-matrices.md`)
