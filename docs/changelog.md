# Jotdex changelog (why things changed)

Short, durable notes for non-obvious fixes and product decisions.
Agents: **read this when debugging related areas**, and **append an entry** when landing a non-trivial fix or behavior change (1–3 sentences of *why*, plus commit hash when known).

Bigger architectural choices still belong in [`docs/decisions/`](decisions/).

---

## 2026-08-08 — Updates tab + Update-Jotdex.ps1; settings tabs

**Settings got crowded, and there was no guided way to pull a new build from GitHub**
- Settings is tabbed (Vault, Network, Security, Notifications, Backup, Updates, Advanced).
- Updates checks GitHub Releases; `Update-Jotdex.ps1` backs up the program to `C:\JotdexBackupHold`, applies the release zip, verifies health, and can roll back. Publish also writes `artifacts\jotdex-win-x64.zip` for Release uploads.

---

## 2026-08-08 — Move-to-another-PC kit

**Moving PCs meant hunting vault + AppData + a separate portable build with unclear restore steps**
- Settings → Move to another PC creates `jotdex-move-*.zip` (vault, auth/config/history, portable app when available, `Restore-Jotdex.ps1`). Dev fallback: `scripts/create-move-kit.ps1`.

---

## 2026-08-08 — Home landing instead of auto-opening a note

**Unlock used to dump you into a random first note; first impression should orient, not surprise**
- Right pane shows a non-editable home: recently viewed / created / updated, open to-dos, and quick actions. Click **Jotdex** in the top bar to return home.

---

## 2026-08-08 — Collapsed Todos ticker polish (`5ed51e9`)

**Label sat at the top of the strip (unlike Folders/Notes), and the loop jumped when it wrapped**
- Move the Todos label to the bottom of the collapsed rail; duplicate equal-height ticker groups so the -50% scroll seam is continuous.
- Portable build republished to `artifacts\win-x64`.

---

## 2026-08-08 — Collapsed Todos title ticker

**When the Todos rail is collapsed, open items were invisible until you expanded again**
- Thin rail scrolls open to-do titles vertically (title only); click the rail to expand. Respects `prefers-reduced-motion`.

---

## 2026-08-08 — Vault-backed Todos + collapsible columns (`85137eb`)

**Wanted glanceable to-dos, more editor width, undo on mis-clicks, and a clear Chrome notification path**
- Open items in vault-root `Todos.md`; done items disappear with a 30s Undo. Desktop rail + mobile tab; Folders/Notes/Todos columns collapse. First Add (and Settings) prompts for browser notifications.
- Portable build republished to `artifacts\win-x64`.

---

## 2026-08-08 — Prompt notifications on first to-do

**Finding the Settings allow button is easy to miss; first Add is a natural user gesture for Chrome’s prompt**
- Adding the first open to-do calls `Notification.requestPermission()`; Settings remains the re-prompt path.

---

## 2026-08-08 — Collapse side columns + todo undo + notify prompt

**Needed more editor width, a safety net for mis-clicks, and a clear Chrome notification allow path**
- Folders and notes columns get the same collapse strip as Todos (desktop; remembered). Completing a to-do still removes it, with a 30s Undo bar.
- Settings → Todo notifications (and turning on a reminder) calls the browser permission prompt; blocked sites need the browser’s site settings.

---

## 2026-08-08 — Vault-backed Todos rail

**Wanted glanceable to-dos beside notes without a separate app or archive pile**
- Open items live in vault-root `Todos.md` (checkbox lines + `jotdex-todo` HTML comments for id/priority/due/remind). Checking done removes the line — no done list.
- Desktop: collapsible right rail; mobile: fourth bottom tab. Browser notifications while the tab can run; catch-up fires at most one alert per to-do.

---

## 2026-08-08 — Collapsing format toolbar while scrolling (`c6f0f45`)

**Sticky formatting bar ate mobile/desktop reading space mid-note**
- Main editor formatting chrome auto-collapses after you scroll down (expands again at the top, or on hover/tap). **Pinned** keeps it open — same idea as pop-out Auto/Pin.
- Portable build republished to `artifacts\win-x64`.

---

## 2026-08-08 — Denser mobile note chrome (`c73a8be`)

**Phone note view had huge gaps under the title/path and empty space beside the editor**
- Mobile-only: collapse default `<p>` margin on the path, tighten action/toolbar spacing, and let the note body use full width.
- Portable build republished to `artifacts\win-x64`.

---

## 2026-08-07 — Phosphor lock/login backdrop (`eeabf49`)

**Wanted a Swordfish-ish tech feel on the password screen without cluttering the form**
- Soft teal code streams (canvas) behind Unlock and Login only; form card stays frosted and readable. Respects `prefers-reduced-motion`.
- Portable build republished to `artifacts\win-x64`.

---

## 2026-08-07 — Mirror schedule stuck + first-run wizard (`5b1fb28`)

**15‑minute mirror looked idle; first-run wizard no longer appeared**
- A mirror run could hang for a long time on iCloud (full-tree ReadOnly walk / robocopy). While `running`, later schedules never start. Removed the pre-walk, capped runs at 12 minutes, quieter robocopy flags; Access Denied dest files are deleted and retried once.
- First-run wizard shows again when no vault is configured (password step optional, password-only, min 6).
- Portable build republished to `artifacts\win-x64`.

---

## 2026-08-07 — Password-only security (no username); removable

**Wanted a simple optional password, not a user account**
- Settings → Security is password-only (set / change / remove). No username in the UI.
- No password → app opens freely. Password set → required on open; **Remove password** turns that off again.
- Idle lock stays opt-in with a choosable minute timeout, only when a password exists.

---

## 2026-08-07 — Password + idle lock live in Settings → Security

**No obvious place to set a password; idle lock should not run without one**
- Settings → Security now creates (or changes) the admin password. Idle lock options appear only after a password exists and default to off.
- Dev mode still skips forced login for APIs, but a password can be set for Unlock / Production use.

---

## 2026-08-07 — Vault mirror ReadOnly fix, PFX copy, idle lock

**iCloud mirror failed with robocopy exit 9; PFX wording was confusing; cookie idle ≠ step-away lock**
- Robocopy now copies data/timestamps only (`/COPY:DT`), clears ReadOnly on write (`/A-:R`), pre-clears ReadOnly on dest, retries more, and surfaces ERROR lines in Settings.
- Settings recommend a distinct mirror folder (`JotdexMirror`) and clarify that self-signed HTTPS needs no PFX (custom PFX + password are optional).
- Frontend idle lock (default 15 min, Settings → Security) covers the app with an Unlock overlay when you stop interacting or leave the tab hidden — autosave no longer keeps you “unlocked.”

---

## 2026-08-07 — AI prompt clipboard fallback + denser pop-out chrome (`f4931c9`)

**AI prompt failed to copy on LAN http://; pop-out header ate note space**
- Clipboard copy falls back when `navigator.clipboard` is blocked (non-HTTPS).
- Pop-out header compacted; AI prompt removed from pop-out (stays on main top bar). Rule added so the AI prompt stays complete when formatting features change.

---

## 2026-08-07 — Pop-out auto-hide tools + Copy AI prompt

**Wanted denser pop-outs and a reusable AI formatting cheat-sheet**
- Pop-out: **Tools: auto/pinned** toggle (default auto) collapses the formatting bar until hover/focus.
- **AI prompt** (top bar + pop-out) copies a clipboard prompt teaching ChatGPT/Claude/etc. Jotdex Markdown (code boxes, callouts, tables, tasks, links).

---

## 2026-08-07 — Fix pop-out window scrolling

**Pop-out note window could not scroll**
- Constrained the pop-out layout so the note body scrolls under a fixed toolbar (overflow was trapped by `overflow: hidden` on the page).

---

## 2026-08-07 — Sticky formatting toolbar while scrolling

**Had to scroll back to the top of a long note to reach Bold/H1/etc.**
- Formatting toolbar (and paste modes) stay sticky at the top of the note pane while scrolling in the main window.

---

## 2026-08-07 — Note toolbar wrap, Pop out, Rescan in Settings

**MacBook note actions were clipped; Rescan felt out of place on the main bar**
- Note action buttons wrap with the pane width (container queries) so Share HTML stays reachable.
- Added **Pop out** for a compact editable floating window (Chrome/Safari browser popup). Closing flushes pending autosave via `keepalive` PUT.
- Moved **Rescan vault** under Settings → Maintenance (reloads folders/notes from disk).

---

## 2026-08-07 — Safari search result clicks (`47f99e2`)

**On Mac Safari, search showed hits but clicking a result did nothing (localhost/Chrome fine)**
- Input `blur` closed/unmounted the dropdown before the result `click` fired. Prevent blur with `mousedown` `preventDefault` on hits, soften blur dismiss, and switch to the editor pane when picking a result.

---

## 2026-08-07 — Guided Windows setup (`4cfcd19`)

**Manual README steps were solid but long for first-time users**
- Added `Setup.cmd` + `scripts\Setup-Jotdex.ps1`: check/install Git, .NET 10 SDK, Node via winget (asks first), create vault, build portable app, optional start + Startup shortcut.
- No random EXE downloads; does not change global execution policy or disable security tools. README leads with this; manual steps remain.

---

## 2026-08-07 — Mobile navigation (desktop unchanged) (`4bf3186`)

**Phone layout was unusable**
- Narrow screens now show one pane at a time with a bottom **Folders / Notes / Note** bar, a ← Notes back control, and larger touch targets.
- Toolbars/actions scroll horizontally instead of crushing the layout. Desktop three-column UI is unchanged above 900px.

---

## 2026-08-07 — IT note templates (network multi-site + more) (`228aaea`)

**Needed a real network doc template for client replacements**
- **Client network** template is table-heavy (WAN, LAN gateways, VLANs, static routes, DNS/DHCP, static hosts, NAT, VPN) with **one site by default**.
- On those notes, **Add site** inserts another full site table block (prompts for the site name).
- Template dropdown was clipped by the middle pane; fixed stacking/overflow so the menu stays visible.

Also: Server build, Client overview, Cutover, Firewall/VPN, M365, Backup/DR, Install, Runbook, Vendor/circuit.

---

## 2026-08-07 — Fewer false “Dense raw HTML” source opens (`82650a1`)

**OneNote-style notes opened in Source for no good reason**
- Example: `3C Camera` is mostly a bullet list wrapped in harmless `<span style=…>` from the OneNote export.
- The old rule treated any 12+ HTML tags as unsafe. TipTap already preserves spans/lists/tables.
- Now only risky tags (div/font/media/etc.) force Source; span/formatting noise stays Visual.

---

## 2026-08-07 — Dual HTTP + self-signed HTTPS (`69d988c`)

**Wanted https:// without making a certificate**
- Settings → Network: **Also listen on HTTPS (self-signed)**. HTTP stays on the main port; HTTPS uses port+1 (configurable).
- App auto-creates `config/jotdex-self-signed.pfx` under the data root. Browser warning is expected — click through.
- Optional custom PFX still supported (used instead of the self-signed cert when set).

---

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
