# Jotdex CHECKLIST

Mark items `- [x]` when done. IDs are stable for chat (“done M1-04”).

**Active milestone:** MDM — Official Tiptap Markdown

---

## S0 — Project agent setup

- [x] `S0-01` AGENTS.md
- [x] `S0-02` `.cursor/rules/jotdex-core.mdc`
- [x] `S0-03` `.cursor/rules/jotdex-workflow.mdc`
- [x] `S0-04` STATUS.md
- [x] `S0-05` CHECKLIST.md with all IDs
- [x] `S0-06` Copy brief to `docs/brief/`
- [x] `S0-07` `docs/vault-format.md`
- [x] `S0-08` `docs/portability.md`
- [x] `S0-09` `docs/architecture/overview.md`
- [x] `S0-10` ADR 0001 stack + layout
- [x] `S0-11` ADR 0002 no in-app importer
- [x] `S0-12` ADR 0003 history + autosave
- [x] `S0-13` ADR 0004 markdown-plus-assets
- [x] `S0-14` ADR 0005 iCloud mirror not live vault
- [x] `S0-15` Threat model stub
- [x] `S0-16` Dir skeleton `src/` `tests/` `tools/` `scripts/`
- [x] `S0-17` .NET 10 SDK verified/installed (10.0.302)
- [x] `S0-18` Node LTS verified/installed (v24.13.0)
- [x] `S0-19` STATUS tooling + workspace reminder complete

---

## M0 — Vault format and fixtures

- [x] `M0-01` Folder/note/assets naming rules
- [x] `M0-02` `.notes-vault.json` schema
- [x] `M0-03` Front matter + `parent_id` subpage rules + preserve unknown
- [x] `M0-04` Windows filename sanitization + collisions
- [x] `M0-05` HTML sidecar naming + embed convention documented
- [x] `M0-06` ≥15 RoundTripFixtures (16 notes)
- [x] `M0-07` `tools/SampleVault/` matching Personal/Technical tree
- [x] `M0-08` `docs/data-safety-tests.md`
- [x] `M0-09` Threat model expanded
- [x] `M0-10` Gate: M0 complete in STATUS

---

## M1 — Windows host and application shell

- [x] `M1-01` Create .NET solution: Server, Core, Infrastructure
- [x] `M1-02` Scaffold React+TS+Vite in `src/Web`
- [x] `M1-03` Serve built SPA from ASP.NET Core
- [x] `M1-04` `GET /api/health`
- [x] `M1-05` Config: vault path + data-root (local disk / portable)
- [x] `M1-06` Structured logging with redaction
- [x] `M1-07` `scripts/publish-win-x64.ps1`
- [x] `M1-08` Service install/uninstall script stubs
- [x] `M1-09` Smoke tests: host starts; health OK; SPA served
- [x] `M1-10` `THIRD_PARTY_NOTICES.md` stub + pin versions
- [x] `M1-11` Gate: STATUS exit report

---

## M2 — Read-only vault engine

- [x] `M2-01` Recursive discovery of notes + folders
- [x] `M2-02` Front-matter parse; preserve unknown keys; derive title/id
- [x] `M2-03` APIs: tree, folder notes, get note (by id)
- [x] `M2-04` Path containment + reject junction/symlink escape
- [x] `M2-05` Markdig render + attachment serving allowlist
- [x] `M2-06` DOMPurify (or equivalent) on client for HTML
- [x] `M2-07` FileSystemWatcher debounce + startup/periodic full rescan
- [x] `M2-08` Three-pane read-only UI shell
- [x] `M2-09` Inline/expandable HTML sidecar display
- [x] `M2-10` Integration tests: traversal blocked; sample vault renders
- [x] `M2-11` Gate: open SampleVault read-only in browser

---

## M3 — Search

- [x] `M3-01` SQLite startup FTS5+trigram capability probe
- [x] `M3-02` Metadata + word FTS + trigram FTS schema
- [x] `M3-03` Index extract: title, path, tags, headings, body, code, attachment names/alt
- [x] `M3-04` Incremental index + full rebuild w/ progress
- [x] `M3-05` Smart search + exact/literal mode
- [x] `M3-06` Filters: folder, tag, title, in:code, has:attachment, modified
- [x] `M3-07` Results UI + Ctrl+K / Ctrl+P
- [x] `M3-08` Prove delete index.db + rebuild equivalent
- [x] `M3-09` Document OCR as future (not V1)
- [x] `M3-10` Gate: technical strings findable on SampleVault

---

## M4 — Safe editing, autosave, history/rollback

- [x] `M4-01` Atomic save
- [x] `M4-02` ETag/hash optimistic concurrency + conflict UX
- [x] `M4-03` Note create + delete (trash); rename/move/duplicate
- [x] `M4-04` Folder create/rename/move/delete
- [x] `M4-05` Assets dir move + relative link rewrite
- [x] `M4-06` Trash outside vault
- [x] `M4-07` Autosave debounce 800–1200ms; status chip
- [x] `M4-08` Saved badge only after server confirms atomic write
- [x] `M4-09` Ctrl/Cmd+S immediate save
- [x] `M4-10` History snapshot on content-changing save; dedupe by hash
- [x] `M4-11` History under AppData `history\{noteId}\`; retention 50 or 30d
- [x] `M4-12` APIs: list / get / restore history
- [x] `M4-13` UI: history panel + Restore (diff/save-as-copy later)
- [x] `M4-14` Restore is undoable (pre-restore snapshot)
- [x] `M4-15` External disk change detection + conflict workflow
- [x] `M4-16` Tests: save, conflict, history
- [x] `M4-17` Gate: accidental edit recoverable; notes valid outside app

---

## M5 — Rich editor + paste

- [x] `M5-01` Tiptap + Markdown pipeline; visual-first UX
- [x] `M5-02` Toolbar: headings, marks, lists/tasks, tables, links, callouts, HR
- [x] `M5-03` Source mode + force-source when unsafe
- [x] `M5-04` Code: language, highlight, copy, paste-as-code, no smart quotes
- [x] `M5-05` Undo/redo, find-in-note, resizable images
- [x] `M5-06` Smart paste Ctrl+V + remote image download (SSRF-safe)
- [x] `M5-07` Plain paste Ctrl+Shift+V
- [x] `M5-08` Visible paste menu (keep / match / plain / code / preserve-page)
- [x] `M5-09` Preserve-page: Markdown + sanitized clipped-page.html + embed
- [x] `M5-10` Screenshot/drop auto-filename into `.assets`
- [x] `M5-11` Non-image attachments + size limit
- [x] `M5-12` CSP + sanitizer; round-trip fixtures green
- [x] `M5-13` Gate: edit without content loss

---

## M6 — Auth and packaging

- [x] `M6-01` First-run: vault path (local disk), admin user, bind/port
- [x] `M6-02` Cookie auth, idle timeout, lockout, password change
- [x] `M6-03` Default bind 127.0.0.1; LAN opt-in; HTTPS/PFX (Kestrel loads PFX on restart)
- [x] `M6-04` Portable ZIP + service install docs
- [x] `M6-05` Upgrade/backup: vault separate from exe *(documented in packaging.md / portability.md)*
- [x] `M6-06` Gate: unauthenticated cannot read notes/attachments

---

## M7 — Static export and hardening

- [x] `M7-01` Static HTML export
- [x] `M7-02` Backup bundle docs + in-app ZIP (`POST /api/admin/backup`)
- [x] `M7-03` Scheduled read-only vault mirror → iCloud (copy, not live)
- [x] `M7-04` Integrity scan
- [x] `M7-05` Maintenance page
- [x] `M7-06` Security suite *(CSP, SSRF, sanitizer, auth gate, path guard — expand later)*
- [x] `M7-07` Gate: static site + restore + mirror documented

---

## M8 — Secrets, ops alerts, TOTP

- [x] `M8-00` README LAN/VPN wording; threat-model defer vault crypto + BitLocker
- [x] `M8-01` ADR: secrets via DPAPI at rest; move-kit unwraps to portable plaintext then rewraps on restore
- [x] `M8-02` `ISecretStore` + DPAPI implementation + export/import portable format + tests
- [x] `M8-03` Wire secrets into move-kit/backup/Restore + docs (seamless transfer; warn ZIP is sensitive)
- [x] `M8-04` Notification channel model + SMTP sender + Settings UI + test send
- [x] `M8-05` Telegram channel (same pipeline)
- [x] `M8-06` Mirror-stale alert job (configurable hours, dedupe)
- [x] `M8-07` TOTP enroll / verify / recovery codes + login + idle unlock
- [x] `M8-08` Gate: password-only still works; secrets never logged; smoke tests for SMTP config round-trip (mock) + TOTP verify

---

## OPS — Offline vault migration

- [x] `OPS-01` Joshua provides OneNote MD export path (do not mutate source)
- [x] `OPS-02` Inventory → `docs/import-format/export-inventory.md`
- [x] `OPS-03` Migration tool/procedure under `tools/MigrateExport/`
- [x] `OPS-04` Dry-run staging vault; validate links/assets
- [x] `OPS-05` Produce real vault; set path; reindex
- [x] `OPS-06` Spot-check; original export unchanged
- [x] `OPS-07` Document migration for future hosts

---

## CB — Multi-provider cloud backup (+ readable vault ZIP)

### Phase 0 — Tracker + ADR

- [x] `CB-00` ADR 0007 + CHECKLIST CB-* + STATUS claim

### Phase 1 — Foundation & security

- [x] `CB-01` Core models/interfaces + settings/state stores (`IncludePlainVaultZip`)
- [x] `CB-02` `ICloudCredentialStore` + exclusion from Move Kit / ExportPortable (tests)
- [x] `CB-03` Streaming JDXK2 + keep JDXK1; decrypt CLI/scripts; crypto tests
- [x] `CB-04` Move Kit staged vault source (internal, not HTTP)
- [x] `CB-05` Snapshot + VaultSnapshotZip + artifact wrapper + generation manifest
- [x] `CB-06` Post-ZIP boundary validation; install/move-kit awareness of new data folders

### Phase 2 — Engine with fake provider

- [x] `CB-10` Fake `ICloudBackupProvider`
- [x] `CB-11` Coordinator (generation, isolation, lock, retention, verification)
- [x] `CB-12` Hosted scheduler (startup catch-up, interval, encryption gate)
- [x] `CB-13` Health aggregation (Move Kit critical / ZIP partial)
- [x] `CB-14` Server API + auth/loopback rules for connect
- [x] `CB-15` Backend tests (coordinator, retention, health, credentials, endpoints)

### Phase 3 — Dropbox

- [x] `CB-20` Dropbox OAuth PKCE + App Folder + credential store
- [x] `CB-21` Dropbox upload sessions + content-hash verification
- [x] `CB-22` Dropbox quota + retention + reconnect
- [x] `CB-23` Dropbox adapter unit tests (mocked HTTP)
- [ ] `CB-24` Dropbox personal-account manual matrix (document results)

### Phase 4 — Google Drive

- [x] `CB-30` Google Desktop OAuth + `drive.file` + credential store
- [x] `CB-31` Resumable uploads + MD5 + SHA-256 verification
- [x] `CB-32` Google quota + folder ID reuse + reconnect
- [x] `CB-33` Google adapter unit tests
- [ ] `CB-34` Google personal-account manual matrix (document results)

### Phase 5 — OneDrive

- [x] `CB-40` MSAL public client + personal accounts + App Folder
- [x] `CB-41` Graph upload sessions + size/hash verification
- [x] `CB-42` OneDrive quota + approot recreation + reconnect
- [x] `CB-43` OneDrive adapter unit tests + preview-permission note
- [ ] `CB-44` OneDrive personal-account manual matrix (document results)

### Phase 6 — UI

- [x] `CB-50` Settings → Backup → Cloud backups (interval, retention, providers, readable ZIP confirm)
- [x] `CB-51` Per-artifact status + storage estimate on provider cards
- [x] `CB-52` Home `CloudBackupHealthBanner` (poll, Open settings, Retry)
- [x] `CB-53` SPA production build + accessible status wording

### Phase 7 — Hardening & release

- [x] `CB-60` Docs (`cloud-backup.md`, backup/portability/threat-model/README/changelog)
- [x] `CB-61` Personal-account matrices + restore test notes
- [x] `CB-62` Packaging/version; confirm vault mirror unchanged; gate complete

---

## CE — Code editing, diagnostics, and reusable snippets

**Active milestone.** CE-00–06 shipped; CE-07 is polish follow-up.

### Foundation

- [x] `CE-00` ADR (`docs/decisions/ADR-code-editor-diagnostics.md`) + checklist IDs
- [x] `CE-01` Tab indentation in inline Tiptap code blocks (`enableTabIndentation`, `tabSize: 4`)
- [x] `CE-02` Lazy CodeMirror 6 Edit dialog; sync to single `codeBlock` via ProseMirror transaction
- [x] `CE-03` Diagnostics: shared model, JSON client lint, PowerShell parse-only API

### Follow-ups

- [x] `CE-04` Reusable snippet notes (`jotdex_type: code-snippet`); Save/Insert; Ctrl+Space from vault index
- [x] `CE-05` On-demand remark-lint note checker (report-only; no format-on-save)
- [x] `CE-06` Optional PSScriptAnalyzer (warnings when module present; parse-only fallback)
- [x] `CE-07` Polish: fold gutter + whitespace in Edit dialog (inline CM rolled back — basic TipTap edit kept)

- [x] `CE-GATE` CE-00–03 verified; changelog; THIRD_PARTY_NOTICES; portable publish ~63 MB zip (+~18 MB vs pre-CE)

---

## ER — Editor reliability

**Active milestone.** Contract: [`docs/decisions/editor-reliability-contract.md`](docs/decisions/editor-reliability-contract.md). ADR: [`docs/decisions/ADR-editor-round-trip-and-paste-transactions.md`](docs/decisions/ADR-editor-round-trip-and-paste-transactions.md).

- [x] `ER-WP0` Vitest/Playwright harness, torture fixture, failing-then-fixed image/heading + reload-policy tests
- [x] `ER-WP1` `EditorMarkdownCodec`, owned block-image serializer, save-safety validator
- [x] `ER-WP2` Deterministic whole-block heading replacement; nested-context refuse partial split
- [x] `ER-WP3` AttachmentResolver; attachment inventory does not `setContent`
- [x] `ER-WP4` PasteSessionManager + pending placeholders; resolve by upload id
- [x] `ER-WP5` Shared code clipboard command; no trim; keep 1.1.23 `insertText`
- [x] `ER-WP6` Revision coordinator + save revision acknowledgment; shared sameness vectors
- [x] `ER-WP7` Docs/ADR/changelog/STATUS; Vitest matrix + Playwright Chromium/Firefox/WebKit; no vault migration

- [x] `ER-GATE` Portable publish 1.1.24; no vault migration (rollback = previous exe)

---

## MDM — Official Tiptap Markdown

Contract: [`docs/decisions/official-tiptap-markdown-migration-contract.md`](docs/decisions/official-tiptap-markdown-migration-contract.md). ADR: [`docs/decisions/0009-official-tiptap-markdown.md`](docs/decisions/0009-official-tiptap-markdown.md).

- [x] `MDM-00` Freeze/recovery: contract copy, 1.1.24 zip retained, `C:\JotdexVault` copied to `C:\JotdexMigration\backup` with SHA-256, spot-restore verified
- [x] `MDM-01` Official-engine spike in isolation; compatibility table; production still legacy
- [x] `MDM-02` Dual codec + differential harness; one engine per editor
- [x] `MDM-03` Jotdex dialect official handlers (images, callouts, styles, task comments, tables, breaks, wikilinks)
- [x] `MDM-04` Typed content insertion helpers + source audit test
- [x] `MDM-05` Official reliability + ephemeral-auth Playwright (not live vault)
- [x] `MDM-06` `markdown:migrate` audit/stage on vault copy; no live apply
- [x] `MDM-07` Official default; soak on staged copy
- [x] `MDM-08` Remove `tiptap-markdown`; 1.2.0 docs/version/local zip (GitHub Release waits for publish)

- [x] `MDM-GATE` Official-only runtime; `npm ls tiptap-markdown` empty; live vault untouched

---
