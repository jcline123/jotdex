# Jotdex STATUS

**Active milestone:** None. Portable **1.2.2** (`v1.2.2` @ `559d061`).  
**Last updated:** 2026-09-02

## In progress

- None. Live vault `C:\JotdexVault` was **not** written.

## Just shipped

- Portable **1.2.2** (`v1.2.2` @ `559d061`) — caret between stacked code boxes / images. Live vault was not rewritten.
- Portable **1.2.1** (`v1.2.1` @ `9472b80`) — Share HTML / static export style Obsidian callouts; titled alert lines export correctly. Live vault was not rewritten.
- Portable **1.2.0** (`v1.2.0` @ `66240be`) — official Markdown engine, Jotdex dialect, `markdown:migrate` (dev-only), collapsed Todos ticker restored. Audit of the vault *copy*: 649 notes, 610 ok, 39 source-only. No live apply.

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

Vault audit (dev machine, copy only):

```powershell
cd src\Web
npm run markdown:migrate -- audit --vault C:\JotdexMigration\backup
```

## Durable memory

- Fix / behavior rationale: [`docs/changelog.md`](docs/changelog.md)
- Official Markdown ADR: [`docs/decisions/0009-official-tiptap-markdown.md`](docs/decisions/0009-official-tiptap-markdown.md)
- Architecture decisions: [`docs/decisions/`](docs/decisions/)
- Code editor ADR: [`docs/decisions/ADR-code-editor-diagnostics.md`](docs/decisions/ADR-code-editor-diagnostics.md)

## Known small polish (not blocking)

- Leftover `tools/SampleVault/Conflict *.md` from earlier conflict tests (safe to delete)
- NuGet NU1903 warning on SQLitePCLRaw (dependency bump later)
- Cloud backup Dropbox/Google live matrices pending (`docs/cloud-backup-matrices.md`)
- Mobile Edit dialog still scrolls the page behind it (accepted for 1.1.20)
- 39 live-vault-copy notes are Source-only (mostly mixed inline image+prose)
