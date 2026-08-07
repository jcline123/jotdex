# Jotdex changelog (why things changed)

Short, durable notes for non-obvious fixes and product decisions.
Agents: **read this when debugging related areas**, and **append an entry** when landing a non-trivial fix or behavior change (1–3 sentences of *why*, plus commit hash when known).

Bigger architectural choices still belong in [`docs/decisions/`](decisions/).

---

## 2026-08-07 — Obsidian-style editor loop + search keys (`c02bb3e`)

**Search needed arrow keys**
- Search dropdown: ↑/↓ move highlight, Enter opens note, Escape closes (also works from the search box).

**Features people use daily in Obsidian**
- `[[` wikilink autocomplete → inserts a relative markdown link.
- **Backlinks** panel (notes that link here).
- **Quick open** with Ctrl+O (title filter, arrow keys + Enter).
- **Outline** of headings with jump-to.
- **Callouts** (Note / Tip / Info / Warning / Danger) from the toolbar.
- **Templates** menu next to New note (Meeting, How-to, Incident, Daily, Blank).
- Click the ▾/▸ gutter on a heading to fold/unfold its section.

---

**Broken screenshots were hard to delete**
- Images are a TipTap node view with hover/selected **Remove**, a clear broken-state card (filename + hint), and Delete/Backspace once selected — no Source mode required.

**Website paste lost structure and pictures**
- Smart / Keep HTML paste now cleans clipboard HTML (headings, lists, tables, links, limited styles) instead of letting markdown transform strip it.
- Inline `data:` images upload into `.assets`; remote `http(s)` images are fetched via `POST /api/notes/{id}/import-image` and rewritten to local attachment URLs (SSRF-safe client, max 20 per paste).

**Paste vanished after image-heavy articles (e.g. UniFi blog)**
- Importing pasted images returned the note’s *old* disk markdown; the parent applied it and reloaded the editor, wiping the paste. Meta updates from upload/import now pass attachments + etag only, not markdown.

---

## 2026-08-07 — History detail, editor color/size, autostart, file logs

**History was timestamps only**
- List API now includes a short diff summary (`+N / −M lines`) and a one-line preview vs the next newer version (or current note). History button toggles the panel closed.

**Headings applied to whole blocks; no color/size**
- H1–H3 with a text selection now split so only the selected words become the heading line (Markdown still needs a full heading line).
- Added text color + font size toolbar controls. These store limited HTML `<span style=…>` in the note (`tiptap-markdown` `html: true`) so they round-trip.

**Reboot persistence / logs**
- Dev runs were not “always on.” Added Settings → **Start with Windows** (user Startup shortcut) and improved `install-service.ps1` (Automatic, no forced `--urls`). Hosting uses `UseWindowsService()`.
- Logs go to `{dataRoot}/logs/jotdex-YYYYMMDD.log` (Notepad-friendly) and **Settings → Logs → View recent log**.

---

## 2026-08-07 — Folder UX, note rename, unbranded share, README

**Share HTML had a “Jotdex” label**
- Removed the brand line from the shared HTML header so emailed notes look generic.

**Folders hard to reorganize**
- Left tree folders are now collapsible (▸/▾), with collapse state remembered in the browser.
- Added **Move** for folders (blank parent = vault root) so e.g. `Work` can sit beside `Joshua's Notebook` instead of inside it. API already existed; UI was missing.

**Note rename wasn’t obvious**
- The old **Move** control mixed rename + folder change in two prompts.
- Split into **Rename** (title/filename only) and **Move** (folder only).

**README too thin for new users**
- Rewrote GitHub README: plain-language “what it is”, step-by-step setup, first actions, LAN tip, where files live.

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
