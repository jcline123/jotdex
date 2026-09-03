# Jotdex STATUS

**Active milestone:** EUX complete — portable **1.3.2**. Baseline was **1.3.1** (`v1.3.1` @ `acfead2`).  
**Last updated:** 2026-09-03

## In progress

- Publishing portable **1.3.2** (`v1.3.2`).

## Just shipped

- Portable **1.3.2** — one Todos rail list (priority, then when added); Share HTML source has no `jotdex-*` classes, data attributes, or comments. Live vault was **not** rewritten. Rollback is the previous exe (1.3.1); keep `artifacts/jotdex-win-x64-1.3.1.zip` and `C:\JotdexBackupHold\jotdex-win-x64-1.3.1.zip`.
- Portable **1.3.1** (`v1.3.1` @ `acfead2`) — Todos rail note chip (open source note) and Add no longer opens Edit. Live vault was **not** rewritten. Rollback is the previous exe (1.3.0); keep `artifacts/jotdex-win-x64-1.3.0.zip` and `C:\JotdexBackupHold\jotdex-win-x64-1.3.0.zip`.
- Portable **1.3.0** (`v1.3.0` @ `3a5b561`) — slash `/`, gutter `+`, bubble formatting, block move, table chrome, image inspector, link popover/bookmarks, Details, highlight/u/sub/sup, alignment comments, titled/collapsible callouts, live outline, Unicode emoji, bundled KaTeX; Snipping Tool paste no longer sticks on “Missing or broken”; long notes with HTML in fences open Visual. Live vault was **not** rewritten. Rollback is the previous exe (1.2.2); keep `artifacts/jotdex-win-x64-1.2.2.zip` and `C:\JotdexBackupHold\jotdex-win-x64-1.2.2.zip`.
- Screenshot paste + visual-first long notes (included in 1.3.0).
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

Isolated passworded Playwright (never the live vault):

```powershell
.\scripts\run-editor-e2e.ps1
```

Vault audit (dev machine, copy only):

```powershell
cd src\Web
npm run markdown:migrate -- audit --vault C:\JotdexMigration\backup
```

EUX-11 audit (2026-09-02): 649 notes, 610 ok, 39 source-only — same as MDM-06. `apply` was not run.

## Durable memory

- Fix / behavior rationale: [`docs/changelog.md`](docs/changelog.md)
- Editor UX contract: [`docs/decisions/editor-ux-expansion-contract.md`](docs/decisions/editor-ux-expansion-contract.md)
- Dialect v2 ADR: [`docs/decisions/0010-editor-ux-and-jotdex-dialect-v2.md`](docs/decisions/0010-editor-ux-and-jotdex-dialect-v2.md)
- Official Markdown ADR: [`docs/decisions/0009-official-tiptap-markdown.md`](docs/decisions/0009-official-tiptap-markdown.md)
- Architecture decisions: [`docs/decisions/`](docs/decisions/)

## Known small polish (not blocking)

- Leftover `tools/SampleVault/Conflict *.md` from earlier conflict tests (safe to delete)
- NuGet NU1903 warning on SQLitePCLRaw (dependency bump later)
- Cloud backup Dropbox/Google live matrices pending (`docs/cloud-backup-matrices.md`)
- One pre-existing unit test fail: `OneDriveCloudBackupProviderTests.Api_401_surfaces_AuthenticationRequired` (not EUX)
- Mobile Edit dialog still scrolls the page behind it (accepted for 1.1.20)
- 39 live-vault-copy notes are Source-only (mostly mixed inline image+prose)
- Bookmark cards do not fetch Open Graph (intentional — no SSRF)
- Block move is ↑/↓ + Alt+Arrow, not native HTML5 drag (Tiptap drag-handle pulls Yjs)
