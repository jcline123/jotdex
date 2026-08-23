# Jotdex changelog (why things changed)

Short, durable notes for non-obvious fixes and product decisions.
Agents: **read this when debugging related areas**, and **append an entry** when landing a non-trivial fix or behavior change (1–3 sentences of *why*, plus commit hash when known).

Bigger architectural choices still belong in [`docs/decisions/`](decisions/).

---

## 2026-08-23 — Portable release 1.1.22 (snippets rail layout)

**Portable release 1.1.22**
- Tag `v1.1.22` — Snippets rail header and list rows no longer stack (notes-list `width: 100%` button CSS was crushing Delete/Refresh/Close).

---

## 2026-08-23 — Portable release 1.1.21 (snippets UX)

**Portable release 1.1.21**
- Tag `v1.1.21` (`7c73538`) — Snippet save fix, reserved `Snippets/` folder, folders-rail Snippets manager (edit in note pane), cursor-aware insert, Insert/Save in Edit dialog, PSScriptAnalyzer module path.

---

## 2026-08-23 — Snippets rail, cursor insert, duplicate shortcut guard

- Insert snippet now splices at the code-box cursor instead of replacing the whole block.
- Notes rail **Snippets** button opens a dedicated manager (edit/delete); snippets stay in `Snippets/` and out of the notes list.
- Clicking a snippet opens it in the main note pane; **Insert** / **Save as snippet** are on inline code boxes and in the **Edit** dialog.
- Saving rejects duplicate shortcuts; double-submit on Save as snippet is guarded. (Earlier failed saves + retry could leave `Title (1).md` duplicates — delete extras in Snippets.)

---

## 2026-08-23 — Snippets folder + save fix + PSA module path

- Saving a snippet failed with “Could not create snippet note” because `NoteCommandService.Create` wrapped a second YAML front matter around an already-complete snippet file, so `jotdex_type` was not on the outer block. Create now has `CreateComplete` for full notes.
- Snippets always go under reserved vault folder `Snippets/`, stay out of the notes list and folders rail (like `Todos.md`), and the save form clarifies Name / Shortcut (Ctrl+Space) / description / tags.
- PSScriptAnalyzer import now resolves the versioned `PSScriptAnalyzer.psd1` under `modules/` so hosted PowerShell finds the Gallery layout after reboot/portable install.

---

## 2026-08-22 — Portable release 1.1.20 (mobile code-box chrome)

**Portable release 1.1.20**
- Tag `v1.1.20` (`4a57b15`) — Code-box snippet/Edit/Copy buttons wrap on phones; mobile folders/notes layout unchanged from 1.1.19.

---

## 2026-08-22 — Portable release 1.1.19 (code editor milestone)

**Portable release 1.1.19**
- Tag `v1.1.19` (`ba0d1cd`) — CodeMirror **Edit** dialog, snippets, Check formatting, optional PSScriptAnalyzer; code boxes stay basic TipTap inline edit.

---

## 2026-08-22 — Code boxes: basic inline edit; Edit dialog only for CodeMirror

**Why**
- Click-to-open inline CodeMirror broke normal typing (keys landed outside the box) and wasn’t what was wanted — keep TipTap/Lowlight for everyday code-box editing; CodeMirror stays behind **Edit**. Also removed the “missing newline at end of block” hint and the parse change that tried to preserve trailing `\n`, which had autosaved an extra blank line into code boxes across the vault.

---

## 2026-08-22 — CE-07 polish + code block trailing newline fix

**Why**
- CE-07 adds fold gutter and optional whitespace display in the **Edit** dialog. (Inline CodeMirror was tried then rolled back — see entry above.)

---

## 2026-08-22 — Snippets, Markdown lint, optional PSScriptAnalyzer (CE-04–06)

**Why**
- Technicians asked for reusable command snippets without a separate database — snippet notes live in the vault (`jotdex_type: code-snippet`) with a rebuildable SQLite index. **Check formatting** uses remark-lint report-only so notes are never silently rewritten. **PSScriptAnalyzer** is optional: when the module is missing the server keeps parse-only PowerShell checks; when present it adds style warnings without executing code.

---

## 2026-08-22 — Advanced code editor + parse-only diagnostics (CE-00–03)

**Why**
- Technicians wanted IDE-like editing and syntax feedback in code boxes without replacing Jotdex’s visual note editor or executing snippets. **CodeMirror 6** (lazy-loaded on **Edit**) fits ProseMirror/TipTap, works on mobile browsers, and stays fully offline — Monaco was rejected for size, workers, and mobile support. PowerShell parse errors use in-process `Parser.ParseInput` (never runs code); JSON checks run in the browser. Markdown fenced blocks remain canonical.

---

**Why**
- The public GitHub repo had no project license (only third-party notices), which left reuse rights unclear. MIT matches the dependency stack and keeps redistribution of the app simple; vault files remain the user’s content.

---

## 2026-08-21 — New note from Home and expanded rail uses folder picker

**Portable release 1.1.18**
- Tag `v1.1.18` (`4e3894d`) — Home and expanded-rail **New note** use the same folder picker as collapsed **Add note**.

**Why**
- The collapsed notes rail already opened `NewNoteModal` (title + folder). Home and the expanded-rail **New note** button still used a title-only prompt, so folder choice was inconsistent. Both now open the same modal; templates still use their own create flow.

---

## 2026-08-17 — Move notes/folders with a folder picker + drag-and-drop

**Portable release 1.1.17**
- Tag `v1.1.17` (`c3fb5ad`) — Move uses a folder-tree picker; drag notes/folders onto the folders rail.

**Why**
- Move used a text prompt, so nested folders were unclear. Settings-style modal now lists the folder tree (filter + expand). Notes and folders can be dragged onto a folder in the existing rail; rest styling is unchanged aside from a drop highlight.

---

## 2026-08-13 — Code-box copy is plain text; strikethrough + clear formatting

**Portable release 1.1.16**
- Tag `v1.1.16` (`39fd1ff`) — selecting in a code box copies/pastes characters only; toolbar Strike + Clear.

**Why**
- Selecting text in a code box still pasted `<!--StartFragment-->` / `<span>` because Chrome copies highlighted HTML from the nested contenteditable (and sometimes puts that HTML in `text/plain`). Copy is intercepted at document capture and rewritten to characters only; paste peels Word/Chrome fragment wrappers (including entity-encoded ones) back to the selected text.

---

## 2026-08-12 — Settings Backup: “Vault mirror” vs cloud backups

**Portable release 1.1.15**
- Tag `v1.1.15` (`e89ac97`) — Backup settings rename **Cloud backup mirror** → **Vault mirror** (copy clarified for local/USB/UNC).

**Why**
- With API **Cloud backups** on the same tab, “Cloud backup mirror” sounded like the same feature. Renamed to **Vault mirror** and rewrote copy around local/USB/UNC destinations so the two paths are easy to tell apart.

---

## 2026-08-12 — Backup Now looked idle while stale OneDrive errors stayed on screen

**Portable release 1.1.14**
- Tag `v1.1.14` (`6e8aebf`) — multi-provider cloud backup (API uploads) + Settings GUI + Home health banner.

**Why**
- “Backup started” could finish in milliseconds with no upload when OneDrive was connected but `enabled: false`, while the UI still showed the previous 403. Filtered Backup Now now auto-enables that provider, clears stale failure text at run start, and polls until the operation finishes (success or real error). OneDrive Settings also links API permissions for Graph consent checks.

---

## 2026-08-12 — OneDrive App Folder quota 403 should not fail backup

**Why**
- After a successful OneDrive Connect, backup runs failed immediately on `GET /me/drive` quota with 403. `Files.ReadWrite.AppFolder` cannot read the full drive; quota is now optional and uploads continue via `special/approot`.

---

**Why**
- Settings sent `oauthClientId`, but System.Text.Json’s camel-case policy expected `oAuthClientId` for `OAuthClientId`, so pasted Azure/Dropbox/Google app IDs never persisted and Connect returned “unavailable.” Explicit `[JsonPropertyName("oauthClientId")]` fixes save/Connect.

---

**Why**
- Providers were gated only on process env client IDs, so Settings showed “Unavailable in this build” with no way to configure real Dropbox/Google/OneDrive from the product. Each provider card now has a setup link, paste fields for App key/Client ID, Save, and Connect (browser OAuth). Client IDs persist in `cloud-backup.json`; env remains an optional override. The temporary local-folder Development fallback was removed.

---

## 2026-08-12 — Development local-folder cloud backup providers

**Why**
- (Superseded by GUI OAuth client ID entry.) Earlier local-folder Development fallback was the wrong product path for enabling real cloud backups.

---

## 2026-08-12 — Cloud backup PKCE OAuth + provider adapters (CB-20…43)

**Why**
- Placeholder OAuth stored the auth code as a refresh token and had no loopback `/oauth/{provider}` callbacks, so real Dropbox/Google/OneDrive connects could not complete. PKCE S256 + token exchange, Dropbox upload sessions/`content_hash`, Google MD5 checks, and mocked HTTP adapter tests make the three providers usable without live accounts; personal matrices remain pending Joshua.

---

## 2026-08-12 — Cloud backup Settings UI + docs (1.1.14, CB-50…CB-62)

**Why**
- Backend cloud backup APIs needed a Settings → Backup section and Home health banner so users can connect providers, see per-artifact status, and recover from failures without reading raw JSON. Docs (`cloud-backup.md`, matrices, backup/portability/threat-model/README) keep the API upload path clearly separate from the filesystem vault mirror.

---

## 2026-08-12 — Cloud backup backend unit tests (CB-15)

**Why**
- CB-02/CB-03 claimed credential exclusion and JDXK2/JDXK1 crypto coverage; CB-15 adds `Jotdex.Unit.Tests` so those behaviors (plus retention, Move Kit critical vs vault-ZIP amber health, Dropbox content-hash vector, settings clamps) are executable and filterable with `--filter CloudBackup`.

---

## 2026-08-12 — Cloud backup folders in install/move-kit (CB-06)

**Why**
- Multi-provider cloud backup adds `config/cloud-backup.json` (portable settings), `secrets/cloud-backup.json` (machine-bound OAuth), `state/cloud-backup/`, and `exports/cloud-backup-staging/`. Setup/Restore/Update now create those dirs; Move Kits and portable secrets export still never pack OAuth, runtime state, or staging — reconnect providers after restore.

---

## 2026-08-12 — Idle lock honors in-app activity for the full timer (1.1.13)

**Portable release 1.1.13**
- Tag `v1.1.13` (`339799b`) — idle lock no longer early-locks via short session cookie while still active.

**Could lock sooner than the configured minutes while still clicking around**
- The UI idle timer correctly reset on mouse/keyboard, but the login cookie was also set to the same short lifetime and only renewed on API calls. Reading or clicking without a save could expire the cookie, return 401, and force the lock early. Cookie lifetime is now the long sliding session again; idle lock (which already signs out) owns the N-minute walk-away behavior.

---

## 2026-08-12 — Edited notes rise to the top; formatting bar stops flashing (1.1.12)

**Portable release 1.1.12**
- Tag `v1.1.12` (`1a1117b`) — edited notes rise in the list; formatting bar collapse no longer flashes.

**Editing a note did not move it up in the folder list**
- The list already sorted by `modified`, but saves left the front-matter timestamp unchanged. Real saves now bump `modified`, and the notes pane refreshes after a successful save so the note floats up (favorites still sort first).

**Formatting bar flashed open/closed while scrolling near the collapse point**
- Collapsing the toolbar changes layout height, which nudged scroll position back across a tight threshold. Wider hysteresis plus a short cooldown after each collapse/expand stops the thrash; it still expands when you scroll back near the top.

---

## 2026-08-11 — Todos rail no longer keeps a 401 error after unlock (1.1.11)

**Portable release 1.1.11**
- Tag `v1.1.11` (`322b667`) — Todos rail ignores idle-lock 401s and reloads after unlock.

**Unlock left “Could not save todos” / “Could not update task” on the rail**
- Same as the note editor: adding or editing a todo while idle-locked hits a signed-out session. The lock overlay already handles that, so the rail now ignores 401s, drops the error as soon as auth is required, and reloads from disk after unlock.

---

## 2026-08-11 — README leads with the product, then install (1.1.10)

**Portable release 1.1.10**
- Tag `v1.1.10` (`979c64b`) — product README; Cloudflare Tunnel next to VPN for remote access.

**GitHub opened on setup steps**
- README now sells what Jotdex is (files you own, one Windows host / any browser, work-note search and capture) before the how-to. Portable zip from Releases is the first install path; clone/build stays for people who want it. Duplicate portability link in “More help” removed. Remote access lists **Cloudflare Tunnel** next to VPN (tunnel to loopback; do not port-forward 5180).

---

## 2026-08-11 — Even line spacing + readable link color (1.1.9)

**Portable release 1.1.9**
- Tag `v1.1.9` (`3df9824`) — readable link color; Shift+Enter = paragraph outside structural blocks; smart paste splits `<br>`.

**Some single-Enter lines sat tighter than others**
- The tight lines were invisible hard breaks (trailing `\` in the vault Markdown) from accidental Shift+Enter or `<br>` in pasted HTML; typed Enters make paragraphs, which space wider. Shift+Enter now acts like Enter in plain paragraphs/headings (still a real line break inside lists, tasks, tables, blockquotes where it's structural), and smart paste splits `<br>` runs into paragraphs. AI prompt documents blank-line paragraphs.

**Links in notes were browser-default blue**
- Unreadable on the dark theme. Added `--link`/`--link-hover` (light blue) applied to links inside the editor.

---

## 2026-08-11 — Shared prefs + unlock to home (1.1.8)

**Portable release 1.1.8**
- Tag `v1.1.8` (`a3d381b`) — idle lock / clip folder / recently viewed on the server; unlock after idle returns to Home.

## 2026-08-11 — Home “Recently viewed” syncs across devices

**Recently viewed was per-browser**
- Created/updated notes and open to-dos already come from the vault. Viewed-note order now lives in `data/config/ui.json` with the other shared prefs, so Home matches on every device. Local recents are uploaded once if the server list is still empty.

---

## 2026-08-11 — Idle lock (and clip folder) stored on the server

**Security settings looked different on Mac vs Windows**
- Idle lock used `localStorage` per browser, while the login cookie still expired at 60 minutes — so Windows showed “off / 15 min” but still locked at an hour. Prefs now live in `data/config/ui.json` and every device loads them from `/api/auth/status`. Cookie lifetime follows the same timer when lock is on (7 days when off). First browser that already had lock enabled migrates into that file.

---

## 2026-08-11 — Unlock after idle returns to home

**Unlock left a Save failed / Note 401 banner on the open note**
- Idle lock signs out the session, so the keystroke that woke the lock also failed a save. After a successful unlock, the main app now clears that error and opens Home instead of the stale editor. 401 on save/load no longer paints an error under the overlay.

---

## 2026-08-10 — Formatting applies to what you selected (1.1.7)

**Portable release 1.1.7**
- Tag `v1.1.7` (`9d5bc8a`) — selection-accurate toolbar formatting.

**Toolbar formatting sometimes hit neighbouring text or left empty lines**
- Triple-click/drag selections silently extend to the start of the next block, so Todo/List/heading converted paragraphs the user never selected. All block and mark commands now trim that overhang first (`selectionUtils.normalizeBlockSelection`).
- Heading-on-selection no longer splits when the selection covers the whole line (the split left empty paragraphs = "space above"); selection whitespace is trimmed before splitting.
- Toolbar buttons preventDefault on mousedown so clicking them can't disturb the selection.

---

## 2026-08-10 — Idle lock always shows lock screen (1.1.6)

**Portable release 1.1.6**
- Tag `v1.1.6` (`1f52c94`) — idle lock returns to full lock screen; session signed out on lock; 401 forces lock UI.

**After idle, the app still looked open and clicks failed**
- First interaction past the idle limit now locks immediately instead of resetting the timer. Lock signs out the session and portals a full-screen lock UI; HTTP 401 also forces the same lock screen when a password is set.

---

## 2026-08-10 — Collapsed rails + inline rename (1.1.5)

**Portable release 1.1.5**
- Tag `v1.1.5` (`213ccf1`) — collapsed rail Add note / folder label, inline note title rename.

**Note title is now click-to-rename**
- Clicking the title in the note header edits it inline (Enter/blur saves, Escape cancels); uses the same rename/move API as the Rename button, which stays.

**Collapsed folders/notes rails felt empty**
- Folders rail shows the selected folder name (short label) toward the top; Notes rail has a compact **Add note** action that opens a modal with a folder dropdown (defaults to the current folder, still changeable). Creating from the collapsed rail leaves the list collapsed.
- Collapsed rail labels share Folders/Notes type; hover soft-highlights the whole rail (Add note gets an extra full-width press cue).

---

## 2026-08-09 — Clip page + Capture polish (1.1.4)

**Portable release 1.1.4**
- Tag `v1.1.4` (`59f6d58`) — Clip page fetch, Capture modal/bookmarklet cleanup, lock-screen copy.

**“From URL” only pasted the link / unclear name**
- Renamed to **Clip page** (new-note menu + open-note action). Fetches the page server-side (SSRF-safe) and inserts title, description, and a plain-text excerpt — browsers cannot read arbitrary sites from the client due to CORS.
- On Windows, page fetch prefers `curl.exe` when present because Cloudflare often challenges .NET `HttpClient`; if fetch still fails, offers URL-only fallback.

---

**Separate Capture top-level screen was confusing**
- Bookmarklet opens the main app with a “Save web clip” modal. In-app: New note ▾ → Clip page…, and Clip page on an open note. Settings → Capture is install instructions only.

---

**Lock screen had casual product copy**
- Removed “Notes stay on this PC…” from the idle lock prompt.

**Bookmarklet that `fetch`es `/api/clip` from other sites cannot work with SameSite cookies**
- Clip flow now opens same-origin `/capture` with title/URL/selection in the URL hash; user confirms folder and saves. Settings → Capture explains Copy-paste setup (Chrome strips dragged `javascript:` links).

---

**Tighter note header + stable formatting bar on short notes**
- Portable release **1.1.3** (`3d4ac22`, tag `v1.1.3`). Actions no longer leave a large empty band beside the title; Auto toolbar uses scroll hysteresis so it stops thrashing on short notes.

---

## 2026-08-09 — Note head density + toolbar scroll thrash

**Title/actions row left a large empty band**
- Actions no longer flex-grow into half the pane; tighter title/button padding.

**Short notes made the formatting bar collapse/expand while scrolling the last line**
- Collapse shortens the page so scrollTop bounced under the threshold. Scroll uses hysteresis (collapse at 72px, expand only under 16px).

**Ship trash browser, vault task rail edits, capture, and home/list hygiene**
- Portable release bumps to **1.1.2** (`fc460fd`, tag `v1.1.2`). Trash is opened from the notes header (removed from the always-on mobile tab bar). Standalone `Todos.md` stays out of notes/home lists via `/api/notes/by-path`. From-notes todos support priority/due/remind; rail refreshes after note trash.

---

## 2026-08-09 — Note-task edit + trash sync

**Deleting a note left stale “From notes” todos**
- Todos rail reloads when notes are trashed/restored/saved (`refreshKey`).

**Could not set priority/due on checklist todos from the rail**
- `POST /api/tasks/update` writes `<!-- jotdex-task … -->` metadata into the source note; rail edit panel matches standalone todos (priority, due, remind, title).

---

## 2026-08-09 — Trash close, Todos rail scroll, hide Todos.md

**Standalone Todos rail went empty after hiding Todos.md**
- `/api/notes` no longer lists `Todos.md`, so the rail’s list lookup created/loaded the wrong note. Lookup is now `GET /api/notes/by-path?path=Todos.md`.

**Trash rail would not leave after Collapse**
- Opening trash also set `mobilePane` to `trash`, so clearing `showTrash` alone left the pane mounted. Close now clears both and uses a labeled Close control.

**“From notes” sat on top of vault tasks**
- Two `.todos-list` flex children each tried to grow; one scroll region now wraps standalone + from-notes sections.

**Todos.md appeared in the date-sorted notes list**
- Standalone `Todos.md` is filtered from `/api/notes` (and the client list) because the Todos rail is the UI for that file.

**Notes title bled under the collapse control**
- Rail-head title truncates in flex before the button instead of painting underneath it.

**Roadmap slice after Dev Startup fix**
- Trash browser (list/restore/as-copy/delete); notes sorted newest-first with favorites; vault-wide open `- [ ]` tasks in Todos with note links; `/capture` + `/api/clip` bookmarklet; history Compare diff; FTS indexes text attachment contents (txt/log/json/csv/html sidecars).

---

**Phone login showed password but clipped the authenticator field (overflow:hidden + keyboard)**
- Auth stage scrolls; MFA is a second step after password (field swaps, not both at once). Settings modal uses a single scroll pane with a fixed height so tabs don’t fight Safari.

---

**LAN worked on the server but other PCs were often blocked by Windows Firewall**
- Saving LAN (Settings, first-run wizard) prompts UAC via `Ensure-JotdexFirewall.ps1` for HTTP/HTTPS allow rules. Decline or no-admin still keeps LAN enabled and shows a manual-port warning. Same helper from Setup/Restore/install-service and portable publish.

---

**Plain move kits in iCloud were risky once they included portable secrets**
- When a password is set, move kits become `.jotdexkit` (AES; unlock password unwraps). `Decrypt-JotdexKit.ps1` for restore.
- Mirror setting can drop one daily kit into `jotdex-move-kits\` for PC-loss recovery without thrashing iCloud on every note file.

---

## 2026-08-08 — DPAPI secrets, ops alerts, optional TOTP (M8)

**Needed a place for SMTP/Telegram secrets and a second factor without vault encryption**
- Secrets at rest use Windows DPAPI; move kits unwrap to `secrets-portable.json` and rewrap on first start.
- Settings → Notifications: SMTP + Telegram + mirror-stale alerts; Security: optional TOTP.
- In-app vault encryption deferred (FTS/history leak); prefer BitLocker — see ADR 0006.

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
