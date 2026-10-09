# Jotdex STATUS

**Active milestone:** INT complete — portable **1.4.2**. Baseline was **1.4.1** (`v1.4.1` @ `9c66ae7`).  
**Last updated:** 2026-10-09

## In progress

- (none)

## Just shipped

- Portable **1.4.2** (`v1.4.2`) — Integrations INT-09: Share HTML/md export; attachment list/upload (`attachments:write`) into note `.assets` with optional placement; Settings create/Edit scopes; OpenAPI/docs. Existing endpoints unchanged. Live vault Markdown was **not** rewritten. Rollback is the previous exe (1.4.1); keep `artifacts/jotdex-win-x64-1.4.1.zip` and `C:\JotdexBackupHold\jotdex-win-x64-1.4.1.zip`.
- Portable **1.4.1** (`v1.4.1` @ `9c66ae7`) — Integrations INT-08: tasks read/write, note insert, changes-since, search snippets; create + Edit/rotate scopes in Settings; OpenAPI/docs. Existing endpoints unchanged. Live vault Markdown was **not** rewritten. Rollback is the previous exe (1.4.0); keep `artifacts/jotdex-win-x64-1.4.0.zip` and `C:\JotdexBackupHold\jotdex-win-x64-1.4.0.zip`.
- Portable **1.4.0** (`v1.4.0` @ `e3449fe`) — Secure Integrations API (scheme-isolated bearer `/api/integrations/v1`, Settings → Integrations, folder ACL, If-Match, idempotency, never-expire option); note Created/Updated chrome + meta poll; provenance FM. Live vault Markdown was **not** rewritten. Rollback is the previous exe (1.3.9); keep `artifacts/jotdex-win-x64-1.3.9.zip` and `C:\JotdexBackupHold\jotdex-win-x64-1.3.9.zip`.
- Portable **1.3.9** (`v1.3.9` @ `d9aa2fd`) — same-note `#fragment` nav in the editor; digit-safe HTML export heading ids; explicit `<a id>` round-trip; callout marker titles no longer eat the next body line. Live vault Markdown was **not** rewritten. Rollback is the previous exe (1.3.8); keep `artifacts/jotdex-win-x64-1.3.8.zip` and `C:\JotdexBackupHold\jotdex-win-x64-1.3.8.zip`.

## Run (Development + SampleVault)

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
cd src\Server
dotnet run --no-launch-profile
```

Listen URL comes from `data/config/network.json` when present (often `http://127.0.0.1:5180`).

Integration smoke (secrets from env only):

```powershell
$env:JOTDEX_BASE_URL="http://127.0.0.1:5180"
$env:JOTDEX_API_TOKEN="jdx_...."
.\scripts\Invoke-JotdexIntegrationSmoke.ps1
```

## Durable memory

- Fix / behavior rationale: [`docs/changelog.md`](docs/changelog.md)
- Integrations ADR: [`docs/decisions/0011-integrations-api.md`](docs/decisions/0011-integrations-api.md)
- Editor UX contract: [`docs/decisions/editor-ux-expansion-contract.md`](docs/decisions/editor-ux-expansion-contract.md)
- Architecture decisions: [`docs/decisions/`](docs/decisions/)

## Known small polish (not blocking)

- Leftover `tools/SampleVault/Conflict *.md` from earlier conflict tests (safe to delete)
- NuGet NU1903 warning on SQLitePCLRaw (dependency bump later)
- Cloud backup Dropbox/Google live matrices pending (`docs/cloud-backup-matrices.md`)
- One pre-existing unit test fail: `OneDriveCloudBackupProviderTests.Api_401_surfaces_AuthenticationRequired` (not INT)
- Grok native menu / Cloudflare Access policy changes are out of scope for INT v1 (documented only)
