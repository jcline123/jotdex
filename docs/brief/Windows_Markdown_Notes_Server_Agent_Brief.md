# Windows Markdown Notes Server

## Agent-Ready Product and Implementation Brief

**Working title:** Windows Markdown Notes Server  
**Target platform:** Windows 11 and Windows Server, x64  
**Client platform:** Modern browser on Windows or macOS  
**Primary use case:** A private, self-hosted technical note system that imports an existing OneNote-to-Markdown export and keeps all durable content in ordinary Markdown and attachment files.

---

## 1. Product objective

Build a Windows-native, self-hosted notes application with a browser interface. The server must run directly on Windows without Docker, Linux, IIS, Node.js, or a separately installed .NET runtime. Users on the local network open the application in Edge, Chrome, Firefox, or Safari.

The application must provide:

1. A OneNote-like folder and note hierarchy.
2. Fast full-text and exact technical search.
3. A rich visual editor backed by Markdown.
4. Excellent code-block handling.
5. Screenshot, image, and file attachment support.
6. Smart rich-text paste, plain-text paste, and paste-as-code.
7. Import of an existing OneNote Markdown export without changing the source export.
8. A read-only static HTML export as a long-term escape hatch.
9. Windows portable mode and Windows Service mode.
10. No content lock-in: Markdown and normal attachment files remain usable if this application is abandoned.

The application database may contain indexes, authentication data, preferences, and caches. It must never be the only location containing note text, images, or attachments.

---

## 2. Non-negotiable architecture rules

### 2.1 Files are the source of truth

All note bodies are stored as UTF-8 `.md` files. Images and attachments are stored as ordinary files. SQLite is a rebuildable index and application-state store, not the canonical note store.

Deleting `index.db` must not destroy content. The application must be able to rebuild the index from the vault.

### 2.2 Windows-native hosting

The production server must be publishable as a self-contained `win-x64` ASP.NET Core application. It must support both:

- Portable console mode: unzip and run the executable.
- Windows Service mode: starts automatically after reboot.

Docker may be used by a developer for optional testing, but no production feature may require Docker or Linux.

### 2.3 Browser-only clients

No client installation is required. The application serves its own frontend. The same URL must work from Windows and macOS browsers.

### 2.4 No silent content loss

The visual editor must never silently discard Markdown, raw HTML, front matter, code, or unknown blocks that it cannot understand. Unsupported content must be preserved as a raw block or force source-edit mode with a clear warning.

### 2.5 Safe, copy-only import

The importer must never alter the user's original OneNote Markdown export. All import work occurs in a staging directory and then copies validated output to the target vault.

### 2.6 One writable server

The initial release supports one authoritative server process writing to a vault. Multi-master sync and concurrent editing from multiple servers are explicitly out of scope.

---

## 3. Recommended technology stack

| Layer | Technology | Reason |
|---|---|---|
| Backend | .NET 10 LTS, ASP.NET Core minimal APIs or controllers | Current Windows-friendly LTS platform; supports self-contained publication and Windows Service hosting |
| Web hosting | Kestrel embedded in the application | Avoids IIS and external web-server requirements |
| Frontend | React, TypeScript, Vite | Mature browser UI stack; production output is static assets served by ASP.NET Core |
| Rich editor | Tiptap with Markdown support | Extensible ProseMirror editor with Markdown parsing/serialization, code blocks, tables, images, and custom nodes |
| Markdown rendering | Markdig on the server | CommonMark-compatible .NET Markdown rendering with useful extensions |
| Search | SQLite FTS5 with a normal word index and a trigram index | Rebuildable local search with relevance ranking and substring search for technical strings |
| SQLite access | Microsoft.Data.Sqlite with an explicitly pinned native SQLite bundle | Simple embedded database; native build must be verified at startup for FTS5 and trigram support |
| Authentication | ASP.NET Core cookie authentication and Identity PasswordHasher | Local account authentication without an external identity provider |
| Browser HTML sanitizing | DOMPurify or an equivalent reviewed sanitizer | Prevents scripts and unsafe attributes in pasted or imported HTML |
| Unit/integration tests | xUnit for .NET; Vitest for frontend | Standard automated test coverage |
| End-to-end tests | Playwright | Browser workflow testing across the actual application |

### 3.1 Dependency rule

Before accepting a package, verify its current license, maintenance status, Windows compatibility, and ability to run without cloud services. Record dependencies and licenses in `THIRD_PARTY_NOTICES.md`.

### 3.2 SQLite startup verification

At startup, run a capability test rather than assuming the packaged SQLite build is correct:

```sql
SELECT sqlite_version();
SELECT sqlite_compileoption_used('ENABLE_FTS5');
CREATE VIRTUAL TABLE temp.fts_probe USING fts5(body, tokenize='trigram');
DROP TABLE temp.fts_probe;
```

If FTS5 or trigram support is unavailable, log a clear diagnostic. Development builds may fall back to a slower exact-file scan, but production packaging must include a compatible SQLite build.

---

## 4. High-level architecture

```text
Windows or macOS browser
        |
        v
React + TypeScript single-page interface
        |
        v
ASP.NET Core API and Kestrel web server
        |
        +-------------------+
        |                   |
        v                   v
Markdown vault          Application data
.md files               config/auth/index/logs
.assets folders          history/trash/export jobs
        |
        v
Optional static HTML export and external backup
```

### 4.1 Runtime components

1. **Web/API host** - serves the frontend, API, attachments, authentication, and health endpoint.
2. **Vault service** - discovers notes, reads metadata, performs safe writes, moves files, and manages attachments.
3. **Index service** - parses notes and updates SQLite search indexes.
4. **File reconciliation service** - watches the vault and performs periodic full scans.
5. **Import service** - scans, stages, validates, and imports Markdown exports.
6. **Static export service** - creates a standalone read-only HTML copy.

---

## 5. File and directory design

### 5.1 Clean separation of vault and application data

The vault contains only user-readable content and a very small optional vault identity file. Indexes, logs, credentials, and transient files live outside the vault.

Example:

```text
D:\NotesVault\
├── Work\
│   ├── Microsoft 365\
│   │   ├── Conditional Access.md
│   │   └── Conditional Access.assets\
│   │       ├── sign-in-log.png
│   │       └── policy-export.json
│   └── Networking\
│       ├── OPNsense IPsec VPN.md
│       └── OPNsense IPsec VPN.assets\
│           └── phase2-settings.png
├── Personal\
│   └── Home.md
└── .notes-vault.json

C:\ProgramData\MarkdownNotesServer\
├── config\
├── auth\
├── indexes\
├── history\
├── trash\
├── logs\
└── exports\
```

Portable mode may use directories beside the executable instead of `C:\ProgramData`, but the logical separation remains the same.

### 5.2 Note format

A note is a standard Markdown file with optional YAML front matter:

```markdown
---
id: 6e1df781-8b61-4d49-919c-48ab469fbc11
title: OPNsense IPsec VPN
created: 2025-03-27T14:00:00Z
modified: 2026-08-06T19:30:00Z
tags:
  - networking
  - vpn
aliases:
  - IPsec Notes
source:
  type: onenote-markdown-export
  original_path: Work/Networking/OPNsense IPsec VPN.md
---

# OPNsense IPsec VPN

The note body begins here.
```

Rules:

- Front matter is optional for externally created notes.
- Unknown front-matter keys must be preserved.
- If `id` is missing, assign one on the first application-managed save.
- Derive the display title in this order: front-matter title, first H1, filename.
- The app may update `modified`; it must preserve `created` when available.
- Use UTF-8 without BOM unless an imported file already requires preservation.

### 5.3 Attachments

Use a sibling assets directory named after the note:

```text
OPNsense IPsec VPN.md
OPNsense IPsec VPN.assets\
```

Use relative Markdown links:

```markdown
![Phase 2 settings](OPNsense%20IPsec%20VPN.assets/phase2-settings.png)

[Download policy export](OPNsense%20IPsec%20VPN.assets/policy-export.json)
```

When a note is renamed or moved, move its sibling assets directory and rewrite the note's relative links. Do not base64-embed normal images in Markdown.

### 5.4 Windows filename handling

Titles may contain characters that are invalid in Windows filenames. Maintain the full title in front matter and sanitize only the physical filename. Handle:

- Invalid characters: `< > : " / \\ | ? *`
- Reserved names such as `CON`, `PRN`, `AUX`, `NUL`, and `COM1`
- Trailing periods and spaces
- Duplicate titles in the same folder
- Maximum path length and excessively long filenames

Do not silently overwrite an existing note. Resolve collisions with a visible suffix and report the change.

### 5.5 Internal links

The canonical portable format is a standard relative Markdown link:

```markdown
[Conditional Access](../Microsoft%20365/Conditional%20Access.md)
```

The editor may offer `[[` autocomplete, but it should serialize resolved links to standard relative Markdown. Preserve unresolved imported wikilinks and display them as unresolved rather than deleting them.

---

## 6. File safety and concurrency

### 6.1 Atomic saves

Never write directly over the active note in a single stream. Use this sequence:

1. Write the new content to a temporary file in the same directory.
2. Flush the stream.
3. Validate that the temporary file can be parsed.
4. Atomically replace or move it over the target file.
5. Update the index only after the file write succeeds.
6. Remove abandoned temporary files during startup reconciliation.

### 6.2 Optimistic concurrency

Return a content hash or ETag when loading a note. The save request must include the original ETag. If the file changed on disk after it was loaded, return a conflict and offer:

- Reload external version
- Save current editor content as a copy
- Compare versions
- Overwrite only after explicit confirmation

### 6.3 External file changes

Use `FileSystemWatcher` for quick updates, but do not rely on it as the only detection mechanism. Debounce repeated events and run a full reconciliation scan at startup, on demand, and periodically. File watcher buffers can overflow or generate duplicate events.

### 6.4 Trash and history

Deleting a note moves the Markdown file and its assets into application-managed trash while preserving the original relative path. Initial defaults:

- Trash retention: 30 days
- History snapshots: last 50 saves or 30 days, whichever is smaller
- History storage is outside the canonical vault

History is a safety feature, not the canonical store. Notes remain fully readable without it.

---

## 7. User interface requirements

### 7.1 Main layout

Use a responsive three-pane desktop layout:

1. **Left pane:** folder tree, tags, favorites, recent notes, trash, settings.
2. **Middle pane:** notes in the selected folder or search results.
3. **Right pane:** note viewer/editor.

On smaller screens, collapse panes into navigable views.

### 7.2 Folder and note operations

Support:

- Create, rename, move, and delete folders
- Create, rename, move, duplicate, and delete notes
- Drag-and-drop move where practical
- Breadcrumb navigation
- Sort by title, modified date, created date, or optional manual order
- Favorites and recently opened notes
- Empty-folder states and clear error messages

Folders are real filesystem directories. Manual display order may use nonessential application metadata; alphabetical order remains the portable fallback.

### 7.3 Keyboard shortcuts

Minimum shortcuts:

| Shortcut | Action |
|---|---|
| `Ctrl+N` | New note in current folder |
| `Ctrl+Shift+N` | New folder |
| `Ctrl+S` | Save immediately |
| `Ctrl+K` | Global search/command palette |
| `Ctrl+F` | Find within current note |
| `Ctrl+Shift+V` | Paste as plain text |
| `Ctrl+Alt+V` | Open paste-options menu |
| `Ctrl+Alt+C` | Insert or convert to code block |
| `Ctrl+P` | Quick-open note |

Use the corresponding Command key shortcuts on macOS browsers where applicable.

### 7.4 Save behavior

- Autosave after approximately 800-1200 ms of inactivity.
- Show `Saving`, `Saved`, `Conflict`, and `Offline/error` status.
- Keep `Ctrl+S` as an immediate explicit save.
- Never show `Saved` until the server confirms the atomic file write.

---

## 8. Rich editor specification

### 8.1 Required formatting

The visual editor must support:

- Headings H1-H6
- Bold, italic, underline, strike-through, and highlight
- Bulleted, numbered, and task lists
- Block quotes
- Horizontal rules
- Links
- Tables
- Inline code
- Fenced code blocks
- Images and image captions
- File attachments
- Callout blocks such as Note, Warning, Tip, and Important
- Collapsible details blocks
- Raw Markdown source mode

Use standard Markdown where possible. Use conservative raw HTML only for features Markdown cannot represent, such as underline, details, or limited style spans.

### 8.2 Code blocks

Code blocks are a first-class feature. Requirements:

- Language selector
- Syntax highlighting
- Preserve tabs, spaces, quotes, backslashes, and line endings
- One-click copy button in view mode
- Paste-as-code command
- Optional line wrapping toggle
- No automatic smart quotes or text substitutions inside code
- Search index must include code content

### 8.3 Markdown round-trip protection

Build a fixture-driven round-trip test suite before enabling general editing. Fixtures must include:

- YAML front matter
- GFM tables
- Fenced code with language names
- Nested lists and task lists
- Inline and block raw HTML
- Images with relative paths
- Links containing spaces and special characters
- OneNote-exported HTML fragments
- Unknown or unsupported blocks

Opening and closing a note without edits must not rewrite it. When edited, the saved result may normalize supported Markdown formatting, but it must preserve semantic content and all unsupported blocks.

If the parser cannot safely represent a note, open it in source mode and clearly explain why. Never provide a visual-save path that would drop content.

### 8.4 Paste modes

Implement four explicit modes:

1. **Smart paste** - preserve supported headings, paragraphs, lists, tables, links, bold/italic, code, and images from clipboard HTML.
2. **Match note formatting** - preserve structure but remove most source styling.
3. **Plain text** - strip all formatting.
4. **Paste as code** - insert exact clipboard text into a fenced code block.

The normal `Ctrl+V` action uses Smart paste. `Ctrl+Shift+V` uses Plain text.

### 8.5 Pasted web content

For copied webpages:

- Sanitize scripts, event handlers, unsafe URLs, forms, iframes, and unsupported styles.
- Preserve useful semantic formatting.
- Preserve the source URL when the clipboard exposes one.
- Convert simple content to Markdown.
- Preserve complex sanitized fragments as a raw HTML/web-clip block rather than flattening or discarding them.
- Detect externally linked images and offer `Make images local`.
- Localized images go into the current note's `.assets` folder.

Remote image downloading must protect against server-side request forgery. Allow only HTTP/HTTPS, limit redirects and file size, validate content type, and block loopback, link-local, private, and internal service destinations unless explicitly configured by an administrator.

### 8.6 Screenshots and file drops

When an image is pasted or dropped:

1. Generate a collision-safe, readable filename.
2. Upload it to the note assets directory.
3. Insert a relative image link at the cursor.
4. Show upload progress and errors.
5. Do not lose the editor selection during upload.

Suggested filename format:

```text
2026-08-06_151522_firewall-error.png
```

For non-image files, create a normal attachment link. Default maximum attachment size should be configurable; start with 100 MB.

### 8.7 HTML safety

All rendered Markdown and HTML must pass through a reviewed allowlist sanitizer before insertion into the browser DOM. Enforce a restrictive Content Security Policy. Do not execute scripts from notes, attachments, pasted HTML, imported HTML, or static exports. Disable inline rendering of SVG by default unless it is sanitized with a dedicated SVG policy.

---

## 9. Search specification

Search is a core feature and must be designed for technical notes, not only prose.

### 9.1 Index design

Maintain two rebuildable FTS5 indexes:

1. **Word index** using a Unicode tokenizer for titles, folder paths, tags, headings, body text, and code.
2. **Trigram index** for literal substring searches such as error codes, registry paths, commands, hostnames, IP addresses, filenames, and partial identifiers.

The authoritative note data remains on disk. The index stores normalized searchable copies and metadata.

Suggested indexed fields:

- Stable note ID
- Title
- Relative path
- Folder path
- Tags
- Headings
- Plain body text
- Code-block text
- Attachment filenames
- Created and modified dates

### 9.2 Search modes

#### Smart search

Default mode performs word and phrase search with relevance ranking. Weight title and tags more heavily than body text.

#### Exact technical search

Provide a visible exact/literal mode for strings such as:

```text
0x80042006
Get-ADUser
HKLM\SOFTWARE\Microsoft
10.0.20.15
ModentoService.exe
```

Use trigram search for queries of three or more characters. For shorter queries, use a bounded direct scan or require more characters.

### 9.3 Initial query syntax

Support these filters in the first complete release:

```text
folder:"Work/Networking" ipsec
tag:sentinelone quarantine
title:"Conditional Access"
in:code Get-ChildItem
has:attachment vpn
modified:30d teams
"0x80042006"
```

The parser must treat malformed filter syntax as ordinary text instead of failing the search.

### 9.4 Search results

Each result displays:

- Note title
- Folder path
- Relevant highlighted snippet
- Matching heading when available
- Tags
- Modified date
- Icons for code and attachments when relevant

Clicking a result opens the note and scrolls to the first match. Keyboard navigation must work.

### 9.5 Performance targets

For a test vault containing up to 25,000 notes and 100,000 attachments on local SSD storage:

- Typical search response: under 200 ms at the server, excluding network latency
- Opening an indexed note: under 300 ms for normal-sized notes
- Incremental reindex after save: under 1 second for normal-sized notes
- Full reindex must show progress and remain cancelable

These are engineering targets, not reasons to compromise data safety.

---

## 10. OneNote Markdown import specification

### 10.1 Import workflow

The user selects the root of the existing Markdown export. The importer performs:

1. **Scan** - enumerate Markdown, images, and attachments.
2. **Analyze** - detect export layout, encoding, front matter, resource folders, duplicate titles, and broken references.
3. **Preview** - show counts, destination paths, warnings, and estimated changes.
4. **Stage** - copy and transform into a temporary staging vault.
5. **Validate** - parse every Markdown file and confirm every local resource reference.
6. **Commit** - move validated content into the destination vault.
7. **Report** - create a human-readable HTML report and machine-readable JSON report.

The original export remains unchanged.

### 10.2 Importer requirements

- Preserve the existing folder hierarchy instead of flattening it.
- Preserve titles, created/modified dates, tags, and source metadata when available.
- Normalize only what is required for valid Windows paths and working links.
- Rewrite image and attachment references to the final relative locations.
- Copy binary files without re-encoding them.
- Resolve internal note links when possible.
- Preserve unresolved links and list them in the report.
- Preserve raw HTML blocks.
- Detect duplicate files by content hash.
- Make repeated imports idempotent through an import manifest and source hashes.
- Support a dry-run mode.
- Allow importing a selected notebook or folder before importing the entire archive.

### 10.3 Import report

The report must include:

- Source root and destination vault
- Start/end times
- Markdown file count
- Successfully imported note count
- Image count and attachment count
- Duplicate names and how they were resolved
- Missing resource references
- Unresolved internal links
- Files skipped and why
- Unsupported or suspicious HTML
- Original and final relative paths
- Content hash for each imported note

### 10.4 Import verification gate

Before full migration, create a pilot import containing representative notes with:

- Screenshots
- Code blocks
- Pasted webpages
- Tables
- Attachments
- Nested folders
- Duplicate page names
- Internal links
- Raw HTML

Do not run the full import until the pilot passes the verification checklist.

---

## 11. API outline

Use stable note IDs in the API, not raw paths supplied by the browser. Validate every resolved path stays inside the configured vault.

Suggested endpoints:

```text
GET    /api/health
GET    /api/vault
GET    /api/tree
GET    /api/folders/{id}/notes
POST   /api/folders
PATCH  /api/folders/{id}
DELETE /api/folders/{id}

GET    /api/notes/{id}
POST   /api/notes
PUT    /api/notes/{id}
POST   /api/notes/{id}/move
POST   /api/notes/{id}/duplicate
DELETE /api/notes/{id}
POST   /api/notes/{id}/restore

POST   /api/notes/{id}/attachments
GET    /api/attachments/{attachmentId}
DELETE /api/attachments/{attachmentId}

GET    /api/search?q=...
POST   /api/admin/reindex
GET    /api/admin/reindex/status

POST   /api/import/scan
POST   /api/import/execute
GET    /api/import/jobs/{id}

POST   /api/export/static
GET    /api/export/jobs/{id}
```

Write endpoints must require authentication, authorization, anti-forgery protection, and appropriate rate limiting. Use ETags or an explicit content version on note updates.

---

## 12. Authentication and security

### 12.1 Initial user model

The MVP is single-user but should not hard-code that assumption into the storage model. Implement one local administrator account with:

- Username and password
- Secure password hashing
- Cookie-based session
- Configurable idle timeout
- Logout from all sessions
- Password-change workflow
- Login rate limiting and temporary lockout

Add TOTP as a later milestone without changing note storage.

### 12.2 Network defaults

- Default portable mode binding: `127.0.0.1` only.
- LAN binding requires an explicit configuration choice.
- Never automatically expose the application to the public Internet.
- Support Kestrel HTTPS with an administrator-supplied PFX certificate.
- Clearly warn when credentials are being used over unencrypted LAN HTTP.
- Document recommended remote access through a VPN or trusted reverse proxy.

### 12.3 Filesystem security

- Run the Windows Service under a dedicated account or a clearly documented service identity.
- Grant that identity only required access to the vault and application-data directories.
- Reject path traversal, alternate data streams, device paths, and symlink/junction escapes from the vault.
- Do not serve arbitrary files by physical path.
- Log metadata and errors, but do not log note bodies, passwords, cookies, or attachment contents.

### 12.4 Attachment serving

- Determine MIME type safely.
- Use `Content-Disposition: attachment` for potentially active file types.
- Permit inline display only for a conservative allowlist such as PNG, JPEG, GIF, WebP, and sanitized PDF handling.
- Add `X-Content-Type-Options: nosniff`.
- Prevent HTML attachments from executing in the application's origin.

---

## 13. Static HTML export

The static export is a read-only safety copy, not the primary editor.

### 13.1 Output requirements

Generate a standalone directory containing:

- Rendered HTML for every note
- Folder navigation
- Relative links between notes
- Copied images and attachments
- Syntax-highlighted code with copy buttons
- Client-side search index
- No server requirement
- No authentication data
- No executable note scripts

Example:

```text
StaticExport\
├── index.html
├── assets\
├── search\
├── Work\
│   └── Networking\
│       └── OPNsense-IPsec-VPN.html
└── Personal\
```

Opening `index.html` from a normal static web server must work. Where browser restrictions prevent full search directly from `file://`, include a small optional Windows static-server executable or document a safe local serving command; the rendered pages themselves must still open individually.

### 13.2 Export consistency

Use the same Markdown rendering rules and sanitizer as the live application. Generate into a temporary directory and atomically replace the previous completed export only after validation.

---

## 14. Windows deployment design

### 14.1 Portable package

Deliver a ZIP containing approximately:

```text
MarkdownNotesServer.exe
appsettings.example.json
start-portable.cmd
install-service.ps1
uninstall-service.ps1
README.md
THIRD_PARTY_NOTICES.md
```

The production executable must contain or serve the built frontend assets. The target computer must not need Node.js or a separately installed .NET runtime.

### 14.2 First-run setup

Portable mode first run should open a setup page that collects:

1. Create or select vault path
2. Create administrator account
3. Select local-only or LAN binding
4. Select port
5. Configure HTTPS certificate if available
6. Optionally scan an existing Markdown export
7. Confirm backup warning and data locations

Secrets must not be stored in plain text in `appsettings.json`.

### 14.3 Windows Service

Provide a supported service installation path. The service must:

- Start automatically after reboot
- Recover after unexpected termination
- Write useful startup failures to Windows Event Log and the rolling application log
- Expose a health endpoint
- Stop gracefully and complete or cancel active writes safely
- Run with an explicitly documented identity and filesystem permissions

### 14.4 Upgrade behavior

- Separate executable files from vault and application data.
- Back up the index and configuration before schema migration.
- Make index schema migrations rebuildable.
- Never require rewriting all Markdown files for an application upgrade.
- Support rollback to the previous executable when no canonical file-format migration occurred.

---

## 15. Repository structure

Recommended layout:

```text
/
├── src/
│   ├── Server/                 # ASP.NET Core host and API
│   ├── Core/                   # Domain models and interfaces
│   ├── Infrastructure/         # Filesystem, SQLite, import, export
│   └── Web/                    # React/TypeScript/Vite frontend
├── tests/
│   ├── Unit/
│   ├── Integration/
│   ├── RoundTripFixtures/
│   └── E2E/
├── tools/
│   └── TestVaultGenerator/
├── docs/
│   ├── architecture/
│   ├── decisions/
│   ├── import-format/
│   └── operations/
├── scripts/
│   ├── publish-win-x64.ps1
│   ├── install-service.ps1
│   └── uninstall-service.ps1
├── .editorconfig
├── Directory.Build.props
├── package.json
├── README.md
└── THIRD_PARTY_NOTICES.md
```

Use architecture decision records for consequential choices, especially Markdown round-tripping, attachment layout, search tokenization, and authentication storage.

---

## 16. Work packages and milestone order

The agent must deliver the system incrementally. Every milestone should leave the repository buildable and the implemented slice demonstrable.

### Milestone 0 - Discovery and test fixtures

**Goal:** Understand the actual export before writing destructive transformation logic.

Tasks:

- Create the repository and architecture decision log.
- Document the expected runtime and data paths.
- Obtain a redacted representative copy of the OneNote Markdown export.
- Inventory export folder conventions, resource folders, front matter, links, raw HTML, and encoding.
- Create round-trip and import fixtures from redacted samples.
- Write a threat model covering local network access, pasted HTML, attachment serving, path traversal, and import handling.

Exit criteria:

- Export-format findings are documented.
- At least 15 representative fixture notes exist.
- Non-negotiable data-safety tests are listed before implementation.

### Milestone 1 - Windows host and application shell

**Goal:** Produce a self-contained Windows application serving a basic browser UI.

Tasks:

- Scaffold .NET backend and React frontend.
- Serve the built SPA from ASP.NET Core.
- Implement configuration loading and portable/service data-root selection.
- Add `/api/health`.
- Add structured logs with redaction.
- Produce a self-contained `win-x64` development publish.
- Add a basic Windows Service installation script.

Exit criteria:

- A clean Windows machine can run the published application without installing Docker, Node, or .NET.
- The browser opens a status page.
- Windows Service mode survives a reboot in a test environment.

### Milestone 2 - Read-only vault engine and navigation

**Goal:** Safely browse an existing Markdown vault without editing it.

Tasks:

- Implement recursive note discovery.
- Parse front matter while preserving unknown keys.
- Derive note IDs and titles.
- Build folder-tree and note-list APIs.
- Render Markdown safely with images and attachments.
- Add path containment and symlink/junction protection.
- Add startup and manual full rescan.
- Add file watcher with debouncing.

Exit criteria:

- The application opens a copy of the user's export read-only.
- Folder hierarchy, Markdown, code, tables, images, and attachments render.
- Malicious HTML and path traversal tests do not execute or escape the vault.

### Milestone 3 - Search

**Goal:** Make search clearly better than the OneNote web experience.

Tasks:

- Create rebuildable SQLite metadata and FTS indexes.
- Add word and trigram indexes.
- Extract headings and code blocks.
- Implement incremental indexing and full rebuild.
- Build smart, phrase, and exact search.
- Add folder/tag/title/code filters.
- Implement result snippets, highlighting, and keyboard navigation.

Exit criteria:

- Searches for ordinary prose and technical strings return correct results.
- Deleting the SQLite index and rebuilding produces equivalent results.
- Search meets the performance target on a generated large test vault.

### Milestone 4 - Safe note and folder editing

**Goal:** Add filesystem-backed create, rename, move, save, trash, and restore.

Tasks:

- Implement atomic writes.
- Add ETag-based concurrency checks.
- Add note and folder operations.
- Add assets-directory move and link rewriting.
- Add trash and basic history.
- Add autosave status and error recovery.
- Add external-change conflict workflow.

Exit criteria:

- Crash and file-lock tests do not corrupt the active note.
- Duplicate names never overwrite content.
- Notes remain valid Markdown when opened outside the application.

### Milestone 5 - Rich editor, code, images, and paste

**Goal:** Deliver the main daily note-taking experience.

Tasks:

- Integrate Tiptap and Markdown parsing/serialization.
- Implement the required formatting toolbar.
- Add source mode.
- Add code language selection, highlighting, and copy buttons.
- Add screenshot paste, image drop, file attachments, and upload progress.
- Add smart, match-formatting, plain-text, and code paste modes.
- Add raw HTML/web-clip block handling.
- Add sanitizer and Content Security Policy.
- Complete Markdown round-trip fixture tests.

Exit criteria:

- Representative OneNote-exported notes can be opened, edited, saved, and reopened without losing content.
- Screenshot paste and paste-as-code are reliable.
- Pasted web content preserves useful formatting without executing scripts.

### Milestone 6 - OneNote Markdown importer

**Goal:** Perform a controlled, reportable migration into a clean vault.

Tasks:

- Implement scan, dry run, staging, validation, commit, and report.
- Preserve folders and metadata.
- Rewrite resource links.
- Resolve internal note links where possible.
- Add duplicate and idempotency handling.
- Produce HTML and JSON reports.
- Run pilot import and compare counts.

Exit criteria:

- Pilot import has no unexplained missing notes or resources.
- Full import can be rerun without multiplying duplicate notes.
- Original export remains byte-for-byte unchanged.

### Milestone 7 - Authentication, setup, and Windows packaging

**Goal:** Make the application safe and practical to operate on a Windows server.

Tasks:

- Add first-run setup and local administrator account.
- Add cookie authentication, session controls, lockout, and rate limiting.
- Add HTTPS/PFX configuration.
- Finalize portable and Windows Service packaging.
- Add Windows Firewall and service-account guidance.
- Add upgrade, backup, restore, and troubleshooting documentation.

Exit criteria:

- Unauthenticated users cannot read notes or attachments.
- LAN setup is documented and tested.
- A nondeveloper can install, run, stop, update, and remove the service.

### Milestone 8 - Static export and operational hardening

**Goal:** Complete portability and disaster-recovery features.

Tasks:

- Implement static HTML export.
- Add export validation and progress.
- Add backup bundle creation for vault plus required application state.
- Add integrity scan for missing attachments and broken internal links.
- Add maintenance page for reindex, history cleanup, trash cleanup, and diagnostics.
- Run full security and recovery test suite.

Exit criteria:

- Static site is browsable and searchable from a simple static web server.
- Restoring a backup to a fresh Windows machine recovers the system.
- The complete definition of done below passes.

---

## 17. Automated test requirements

### 17.1 Unit tests

Cover at minimum:

- Windows filename sanitization and collision handling
- Vault path containment
- Front-matter parsing and preservation
- Markdown link parsing and rewriting
- Query parser behavior
- Search normalization
- Import manifest and idempotency
- ETag conflict detection
- MIME and attachment policy
- Remote-image URL validation

### 17.2 Integration tests

Use temporary vaults to test:

- Atomic save and simulated crash
- Rename/move with assets
- Trash and restore
- File watcher event storms and missed-event reconciliation
- Index rebuild
- External edits while a note is open
- Locked files and antivirus-like transient access failures
- Import staging rollback

### 17.3 Round-trip golden tests

For each fixture:

1. Parse Markdown.
2. Load it into the visual-editor model.
3. Serialize without user edits.
4. Compare semantic output and preserved raw blocks.
5. Render both versions and compare required structure.

Unknown content must never disappear.

### 17.4 End-to-end browser tests

Automate:

- First-run setup
- Login/logout
- Folder and note creation
- Edit and autosave
- Code paste
- Screenshot paste
- File attachment
- Smart and plain-text paste
- Search and exact search
- Move/rename/delete/restore
- Conflict workflow
- Import dry run and pilot import
- Static export

### 17.5 Security tests

Include:

- Stored and reflected XSS
- Malicious pasted HTML
- Malicious imported Markdown/HTML
- Path traversal and encoded traversal
- Symlink/junction escape
- SVG script payloads
- HTML attachment origin isolation
- CSRF
- Login brute force
- Remote image SSRF
- Oversized upload and decompression/zip-bomb protections where archives are accepted

---

## 18. Definition of done for version 1

Version 1 is complete only when all of the following are true:

1. The server runs directly on Windows without Docker, Linux, IIS, Node.js, or a separately installed .NET runtime.
2. A Windows or macOS browser can use the full interface.
3. Existing Markdown folders can be opened and browsed without conversion.
4. The OneNote Markdown export can be imported through a dry-run and validated workflow.
5. Folders, note titles, images, attachments, code, tables, and links are preserved to the documented extent.
6. Notes can be created and edited visually or as raw Markdown.
7. Code blocks preserve exact text and offer syntax highlighting and copying.
8. Screenshots can be pasted directly into notes.
9. Smart, plain-text, and code paste modes work.
10. Search handles both ordinary language and technical substrings such as error codes and registry paths.
11. All note text and attachments remain ordinary files outside the database.
12. The search database can be deleted and rebuilt.
13. Saves are atomic and external-change conflicts are not silently overwritten.
14. Authentication protects notes and attachments.
15. The application can run as a Windows Service and recover after reboot.
16. A static HTML export can be generated.
17. A backup can be restored to a clean Windows machine.
18. Security, round-trip, integration, and end-to-end test suites pass.
19. Operational documentation is sufficient for a nondeveloper administrator.
20. No known operation silently drops user content.

---

## 19. Explicitly out of scope for version 1

Do not expand version 1 to include:

- OneNote-style infinite canvas positioning
- Handwriting or ink editing
- Real-time collaborative editing
- Native iOS, Android, macOS, or Windows desktop clients
- Multi-master sync
- AI summarization or chat
- OCR over every screenshot
- Browser extensions
- Public Internet hosting automation
- End-to-end per-note encryption
- Multiple independent tenant organizations

The file format and APIs should not make these impossible later, but they are not version 1 deliverables.

---

## 20. Agent execution instructions

1. Begin with Milestone 0 and Milestone 1 only. Do not attempt the full rich editor before the export fixtures and file-safety rules exist.
2. Make a small, reviewable commit or pull request for each coherent task.
3. Keep a running `STATUS.md` containing completed work, current risks, next steps, and commands required to run the project.
4. Record consequential architectural choices in `docs/decisions`.
5. Do not replace the Markdown vault with database-owned content.
6. Do not silently normalize or delete unknown Markdown.
7. Do not mutate the user's source export.
8. Do not add cloud dependencies or telemetry.
9. Stop and report before any design change that violates a non-negotiable rule.
10. At the end of each milestone, provide:
   - Build and run commands
   - Published artifact path
   - Test results
   - Known limitations
   - Screenshots of the implemented workflow
   - Exact next milestone proposal

---

## 21. Initial kickoff prompt for a coding agent

Copy the following prompt into the coding agent after providing this specification and a redacted sample export:

```text
You are implementing the Windows Markdown Notes Server described in Windows_Markdown_Notes_Server_Agent_Brief.md.

Start with Milestone 0 and Milestone 1 only. Do not build the rich editor, importer execution, or authentication yet.

Your first deliverable must:
1. Create the repository structure in the specification.
2. Scaffold a .NET 10 ASP.NET Core backend and React/TypeScript/Vite frontend.
3. Serve the built frontend from the ASP.NET Core application.
4. Add GET /api/health with version, uptime, and non-sensitive runtime status.
5. Implement portable-mode and service-mode data-root resolution interfaces.
6. Add safe structured logging that does not log note content.
7. Add a self-contained win-x64 publish script.
8. Add initial Windows Service install/uninstall scripts.
9. Create STATUS.md and the first architecture decision records.
10. Analyze the supplied redacted OneNote Markdown export and document its folder, front-matter, resource, link, HTML, and encoding patterns without changing the export.
11. Create at least 15 redacted round-trip/import fixtures from the supplied samples.
12. Add automated smoke tests proving the API starts and the SPA is served.

Non-negotiable constraints:
- No Docker or Linux runtime requirement.
- No note content stored only in a database.
- No mutation of the source export.
- No silent loss of unsupported Markdown or HTML.
- No cloud services or telemetry.

At completion, stop and report:
- Files created and architecture chosen
- Build, test, run, and publish commands
- Location of the win-x64 published output
- Results of the export-format analysis
- Risks or unknowns discovered
- Test results
- Proposed Milestone 2 task list

Do not begin Milestone 2 until the Milestone 0/1 deliverable has been reviewed.
```

---

## 22. Reference implementation notes

The selected architecture is intentional:

- ASP.NET Core can run directly as a Windows Service without IIS.
- .NET supports self-contained and single-file Windows publication.
- Tiptap provides Markdown parsing/serialization and extensible nodes, including code and file-handling support.
- SQLite FTS5 supports ranked full-text search, and its trigram tokenizer supports substring matching.
- `FileSystemWatcher` is useful for prompt updates but must be backed by reconciliation because events can be duplicated or lost.
- Markdig provides an extensible CommonMark-compatible renderer for .NET.

Pin exact package versions in the repository lockfiles after the initial compatibility and license review. Do not leave production dependencies floating.
