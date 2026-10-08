# ADR 0011 — Secure Integrations API and note provenance

**Status:** Accepted  
**Date:** 2026-10-08  
**Milestone:** INT

## Context

Authorized automation (e.g. Grok Bot) must search, read, create, append, and update selected notes through the existing Cloudflare Tunnel without sharing the admin password or browser session. Human Access login and cookie auth must remain unchanged. Ordinary editor saves must not be labeled as “API” edits.

## Decision

### Route and scheme separation

| Surface | Path | Auth |
|---------|------|------|
| External API | `/api/integrations/v1/*` | Scheme `JotdexIntegrationToken` (Bearer) only |
| Admin | `/api/admin/integrations*` | Cookie `jotdex_session` + CSRF + local password required |
| Browser APIs | `/api/notes`, settings, etc. | Cookie only — bearer must not satisfy |

`UseJotdexAuthGate` is scheme-aware. Integration auth is never bypassed in Development and never inherits open-access (no password) behavior.

### Token storage

Under the app data root (not the vault, not search indexes):

- `data/config/integrations.json` — enabled flag, kill-switch, rate limits
- `data/integrations/tokens.json` — id, display name, SHA-256 verifier, scopes, folders, vaultId, timestamps
- `data/integrations/activity/` — bounded audit (no secrets/bodies)
- `data/integrations/idempotency/` — create/append keys, 24h retention

Secrets are shown once; only verifiers are stored. Tokens bind to `.notes-vault.json` `id`. Expiration defaults to 90 days (max 365); optional **never expire** stores `ExpiresAt = MaxValue` and remains valid until revoked.

### Provenance

Additive front-matter keys on managed writes: `jotdex_created_via`, `jotdex_created_by`, `jotdex_updated_via`, `jotdex_updated_by`, `jotdex_last_api_update_at`, `jotdex_last_api_update_by`, `jotdex_provenance_hash`. Actor comes from `NoteChangeContext` (server auth state), never client-supplied fields. `DocumentSameness` ignores these keys (and `modified`) so autosave loops cannot start.

### Move-kit / restore

Preferences in `integrations.json` may travel; restore defaults **integrations disabled** and requires token re-issue. Token verifiers are not silently activated on restore.

## Consequences

- CSRF antiforgery is introduced for admin integration mutations (first CSRF surface).
- Vault-wide write coordinator serializes browser and API mutations.
- MCP / Cloudflare Worker / OAuth AS are out of scope for v1.
