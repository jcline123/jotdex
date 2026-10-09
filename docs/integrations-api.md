# Jotdex Integrations API

Disabled-by-default REST surface for automation (for example Grok Bot) through Cloudflare Tunnel. Browser cookie login is unchanged. Design: [ADR 0011](decisions/0011-integrations-api.md).

**Base path:** `/api/integrations/v1`  
**Auth:** `Authorization: Bearer <jotdex-api-token>` only (scheme `JotdexIntegrationToken`). Cookies never unlock this surface.

## Enable and issue a token

1. Set a local administrator password (Settings → Security).
2. Settings → **Integrations** → enable Integrations.
3. Create a token with a preset (Read / Capture+append / Read+edit), optional attachments read/write / **tasks** / **insert** scopes, folders or explicit whole-vault, expiration ≤ 365 days, or **Never expire** (still revocable anytime).
4. Confirm with the admin password (and TOTP if enabled).
5. Copy the secret once (`jdx_…`). Store it only in your bot’s secret store.

**Edit** on an active token loads its scopes/folders into the form; **Save and rotate** issues a new secret with the updated scopes (update your bot). Revoke from the same page. Removing the local password is blocked while Integrations is enabled.

## Cloudflare three-header model

Use a **separate Cloudflare Access application** for the API path with **Service Auth** (client credentials), not the human Access app used for the browser UI.

| Header | Purpose |
|--------|---------|
| `CF-Access-Client-Id` | Cloudflare Access service token id |
| `CF-Access-Client-Secret` | Cloudflare Access service token secret |
| `Authorization` | `Bearer` + Jotdex API token |

Suggested path policy:

- Human Access app → SPA + `/api/*` except integrations (cookie session).
- API Access app (Service Auth) → `/api/integrations/v1/*` only.

Jotdex does not configure Cloudflare for you. **Grok native menu integration is not verified** in this release; use any HTTPS client that can send the three headers.

## Endpoints (summary)

Authenticated OpenAPI sketch: `GET /api/integrations/v1/openapi.json`.

| Method | Path | Scope |
|--------|------|--------|
| GET | `/whoami` | any valid token |
| GET | `/folders` | `notes:read` |
| GET | `/notes` | `notes:read` |
| GET | `/notes/changes?since=` | `notes:read` |
| GET | `/search?q=` | `notes:read` (optional `includeSnippets=false`) |
| GET | `/notes/{id}` | `notes:read` |
| GET | `/notes/{id}/export?format=html\|md` | `notes:read` |
| GET | `/notes/{id}/attachments` | `attachments:read` |
| GET | `/notes/{id}/attachments/{attachmentId}` | `attachments:read` |
| GET | `/tasks` | `tasks:read` |
| POST | `/notes` | `notes:create` + `Idempotency-Key` |
| PUT | `/notes/{id}` | `notes:update` + `If-Match` |
| POST | `/notes/{id}/append` | `notes:append` + `Idempotency-Key` + `If-Match` |
| POST | `/notes/{id}/insert` | `notes:insert` or `notes:append` + `Idempotency-Key` + `If-Match` |
| POST | `/notes/{id}/attachments` | `attachments:write` + `Idempotency-Key` (+ `If-Match` when placement ≠ none) |
| POST | `/tasks` | `tasks:write` + whole-vault + `Idempotency-Key` |
| PATCH | `/tasks/{id}` | `tasks:write` + `If-Match` (note/Todos.md etag) |

Write bodies for notes accept `bodyMarkdown` only (merged into existing front matter), except insert which uses `{ markdown, position, heading?, occurrence? }`. Clients cannot set ids, dates, or attribution. Updates require exact `If-Match` ETag → `428` if missing, `412` if stale. Create/append/insert/task-create require `Idempotency-Key` (24h, same key + different body → `409`).

### Tasks

- `GET /tasks?status=open|done|all` (default `open`), optional `folder` / `folderId`, `updatedSince`, `limit`, `cursor`.
- Each task includes `id`, `text`, `done`, `dueDate`, `source` (`todo-list` \| `note`), note fields for note tasks, `line`, containing-file `etag`, timestamps.
- Folder ACL applies to note tasks. Standalone `Todos.md` tasks are returned **only** for whole-vault tokens.
- `POST /tasks` adds a standalone todo (whole-vault required). `PATCH /tasks/{id}` toggles done / edits text for note or todo-list tasks; only that checkbox line changes.

### Insert

`POST /notes/{id}/insert` with `position=top|afterHeading`. Top inserts after the first H1 when present; `afterHeading` inserts below the matching heading (`occurrence` default 1). Missing heading → `404` `heading_not_found`.

### Changes since

`GET /notes/changes?since=<ISO>` returns created/updated notes (newest first) with `nextCursor` and `nextSince`. Deleted ids are reserved (`deleted: []` until tracked). Folder ACL applies.

### Search snippets

Search hits keep the existing `snippet` field and add `snippets` (up to 3 short matches with nearest heading). Pass `includeSnippets=false` to omit the array.

### HTML / Markdown export

`GET /notes/{id}/export?format=html` returns the same self-contained Share HTML the app downloads (inline CSS, images as data URIs, no external requests, front matter / API metadata excluded). Optional `theme=light|dark` (default `light`) and `includeTitle=true|false` (default `true`). `format=md` returns the raw Markdown body (no front matter). `format=pdf` is not available. Response headers: `Content-Type`, `Content-Disposition: attachment; filename="…"`, `X-Jotdex-Etag`.

### Attachments

- `GET /notes/{id}/attachments` lists `attachmentId`, `filename`, `contentType`, `size`, `createdAt`.
- `POST /notes/{id}/attachments` is `multipart/form-data` with `file` (required) and optional `filename`, `altText`, `placement` (`none`|`append`|`top`|`afterHeading`), `heading`. Stores under `{Note}.assets/` like the editor. Allowed: png, jpg/jpeg, gif, webp, pdf, docx, xlsx, txt, csv (magic-byte checked). Size limit matches the app (`MaxAttachmentBytes`, default 100 MB). Response includes a ready-to-paste `markdown` snippet (`![](…)` for images, `[…](…)` otherwise). Placement inserts that snippet via append/insert (If-Match required; 428/412).

Limits (defaults): ~120 read/min/token, ~20 write/min/token, 2 MiB JSON body (multipart uploads use the attachment size limit), page size ≤ 100. Over limit → `429` + `Retry-After`.

Folder ACL is enforced on every path. Out-of-scope ids return `404` (no title leak). Missing scope → `403`. Responses use `Cache-Control: no-store`.

## Provenance

Managed writes add additive front-matter keys (`jotdex_created_via`, `jotdex_updated_via`, …). The note chrome shows Created / Updated (and “via API (Name)” when applicable). Metadata is not part of Share HTML or copied Markdown body text.

## Move / restore

`config/integrations.json` preferences may travel in a move kit. Token verifiers are **not** activated on restore — Integrations is left disabled; re-issue tokens on the new PC. See [portability.md](portability.md) and [backup.md](backup.md).

## Sample client

```powershell
$env:JOTDEX_BASE_URL = "https://notes.example.com"
$env:JOTDEX_API_TOKEN = "jdx_...."
# Optional Cloudflare Service Auth:
# $env:CF_ACCESS_CLIENT_ID = "..."
# $env:CF_ACCESS_CLIENT_SECRET = "..."
.\scripts\Invoke-JotdexIntegrationSmoke.ps1
```

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| `401` on `/api/integrations/v1/*` | Feature off, bad/expired/revoked token, wrong vault id, or cookie used instead of Bearer |
| `401` on `/api/notes` with Bearer | Expected — browser APIs are cookie-only |
| `403` `forbidden_scope` | Token missing the required scope |
| `404` on a note you know exists | Folder not in token allow-list (or sibling-prefix mismatch) |
| `428` / `412` | Missing or stale `If-Match` |
| `409` idempotency | Same `Idempotency-Key` with a different body |
| `429` | Per-token rate limit |
| `503` `disabled` | Integrations toggled off |
| Cloudflare `302`/`403` before Jotdex | Service Auth app path or credentials wrong |
