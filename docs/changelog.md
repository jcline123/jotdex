# Jotdex changelog (why things changed)

Short, durable notes for non-obvious fixes and product decisions.
Agents: **read this when debugging related areas**, and **append an entry** when landing a non-trivial fix or behavior change (1–3 sentences of *why*, plus commit hash when known).

Bigger architectural choices still belong in [`docs/decisions/`](decisions/).

---

## 2026-08-07 — Share HTML, LAN bind, drag-drop images (`e430416`)

**LAN stopped working after restarts**
- Saved Settings had `bindMode: lan` in `network.json`, but `dotnet run` injects `ASPNETCORE_URLS` from launchSettings (`127.0.0.1`), which used to win and ignore LAN.
- Fix: when `network.json` exists, prefer it (clear launchSettings URLs). Explicit override only with `JOTDEX_FORCE_URLS=1`. Removed forced `applicationUrl` from launchSettings.
- In-app **Restart server** already cleared URL overrides; the bug showed up mainly on agent/dev `dotnet run` restarts.

**Export HTML in the top bar**
- That control exported the **entire vault** to a static HTML site under app data (`exports/static`), not a single note.
- Moved to **Settings → Maintenance → Export vault as HTML**.

**Share one note**
- Added note action **Share HTML** → `GET /api/notes/{id}/export-html`.
- Downloads one self-contained `.html` file with CSS and local images inlined as data URIs (for email/offline). Remote images still need **Make images local** first.

**Drag-drop images looked broken**
- Drops *did* upload into `.assets`; display failed because macOS screenshot names often contain a **narrow no-break space** (U+202F) before `PM`, which broke markdown/HTML image URLs (looked like `?PM`).
- Fix: sanitize attachment filenames (normalize odd whitespace; strip URL-significant chars), encode markdown asset paths, harden drop to use `dataTransfer.items`, rewrite `.assets/…` links to `/api/attachments/{id}` when opening notes.

---

## 2026-08-07 — Autosave ETag loop (`159886b`)

**Symptom:** save chip flickered Editing/Saving, or stuck on Saving; sometimes “note changed on disk.”

**Root cause:** ASP.NET camelCase serializes `ETag` as `eTag`, but the SPA read `etag`. The client never kept a real ETag, every save got HTTP 409, and conflict retry looped hard.

**Fix:**
- Serialize as `etag` on save results, note details, and related APIs (`[JsonPropertyName("etag")]`).
- Treat empty expected ETag as “no concurrency check” (first save / older client).
- Cap client conflict retries; single ~1s debounced autosave; `sameMarkdown` / server `SameDocument` ignore cosmetic TipTap re-serialization so echoes don’t re-dirty the note.

---

## 2026-08-07 — V1 ship (`08bddb9`)

Initial published V1: local vault + optional cloud mirror, TipTap editor, search, auth, HTTPS PFX, backup ZIP, static vault export, restart button, code boxes, etc. See `CHECKLIST.md` / `STATUS.md`.
