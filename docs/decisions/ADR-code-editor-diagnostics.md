# ADR: Advanced code editor, diagnostics, and snippets

## Status

Accepted (CE-00)

## Context

Jotdex code blocks today use TipTap `CodeBlockLowlight` with a custom React node view (`CodeBlockView.tsx`): language selector, Lowlight/Highlight.js coloring, and plain-text copy. That gives **syntax highlighting** only — no parse validation, IDE-style editing, or reusable snippet library.

Technicians want Obsidian-like code editing (spacing checks, reusable commands, better editing) without replacing Jotdex’s visual note editor or violating file-canonical / no-execution rules.

## Current architecture

| Layer | Role |
|---|---|
| TipTap `codeBlock` node | Canonical ProseMirror node; serializes to fenced Markdown |
| `CodeBlockLowlight` + Lowlight | Lightweight in-note view with Highlight.js |
| `CodeBlockView` | Chrome: language `<select>`, Copy; `NodeViewContent` for inline edit |
| `@tiptap/markdown` (official, 3.29.2) | Round-trip to vault `.md` via `EditorMarkdownCodec` |
| Autosave | Debounced `onUpdate` → `PUT /api/notes/{id}` |

## Decision: CodeMirror 6 (not Monaco)

**CodeMirror 6** for the optional **Edit** dialog only.

| Criterion | CodeMirror 6 | Monaco |
|---|---|---|
| ProseMirror integration | Official MIT example for embedded CM in code blocks | Heavier “mini VS Code” embed |
| Mobile browsers | Supported | Officially not supported |
| Offline / self-hosted | npm packages bundled in Vite build | Obsidian plugins often load hosted Monaco |
| Bundle | Modular; lazy-loaded on Edit | Large; workers for language services |
| Lint / autocomplete | Official `@codemirror/lint`, `@codemirror/autocomplete` | Strong but LSP-oriented |

Monaco is deferred. Jotdex must work from phones on the LAN; Monaco’s mobile stance and worker model are poor fits.

## Lazy-loading strategy

- **Default view:** existing Lowlight block (unchanged).
- **Edit:** one lazy-loaded `CodeEditorDialog` + CodeMirror instance per open dialog.
- **Never** mount CodeMirror for every code block on a note.
- Vite `import()` splits CM packages into a separate chunk loaded on first Edit.

## Synchronization

Dialog edits update **only** the targeted `codeBlock` node via a ProseMirror transaction (`replaceWith` on the node’s text content). The TipTap editor’s normal `onUpdate` path remains the sole save pipeline — no direct note API writes from the dialog.

On open: `editor.setEditable(false)` so the note behind the modal is not edited independently. On close: flush pending sync, restore editability.

Stale positions: if `getPos()` no longer resolves to a `codeBlock`, sync stops and the user sees a non-destructive error.

## Markdown remains canonical

Stored form stays ordinary fenced blocks:

```markdown
```powershell
Get-Service Spooler
```
```

No custom fence metadata (titles, fold lines, highlight ranges) in CE. Language attribute maps to the fence info string only.

## Diagnostic architecture

Shared model (`CodeDiagnostic`: source, severity, message, optional code, line/column range).

| Language | Phase | Mechanism |
|---|---|---|
| JSON | CE-03 | Client `JSON.parse` + position mapping |
| PowerShell | CE-03 | Server `Parser.ParseInput` (parse-only, in-process) |
| Style (tabs, trailing space) | CE-03+ | Client warnings in dialog; non-blocking |
| PSScriptAnalyzer | CE-06 (future) | Optional; evaluate size/packaging |
| remark-lint (note Markdown) | CE-05 (future) | On-demand report only; no auto-fix |

**Security boundary:** diagnostics are **parse-only**. Never `Invoke`, `Process.Start`, `pwsh.exe`, module import for execution, temp script files, or logging of submitted code.

PowerShell endpoint: authenticated like other `/api/*` when password is set; max input size; bounded timeout; cancellation.

## Future snippet architecture (CE-04)

Snippets are normal vault notes with front matter:

```yaml
jotdex_type: code-snippet
jotdex_language: powershell
jotdex_trigger: restart-spooler
```

Body contains a fenced code block. SQLite may index triggers for search; Markdown remains authoritative. No execution, no proprietary snippet DB.

## Mobile and accessibility fallback

- View/copy/language change work without CodeMirror.
- Edit dialog is keyboard-accessible (focus trap, Escape, Done).
- If CM fails to load, user keeps inline Lowlight editing.

## Dependencies and portable-build impact

**Frontend (lazy chunk):** `@codemirror/*`, `@lezer/highlight`, `@codemirror/legacy-modes` — MIT.

**Backend:** `Microsoft.PowerShell.SDK` 7.5.4 (MIT) in a separate `Jotdex.PowerShellDiagnostics` assembly (avoids Markdig.Signed vs Markdig conflict in Infrastructure). Parse-only via `Parser.ParseInput`. Portable zip grew from ~45 MB to ~63 MB (~+18 MB) in the first CE publish with PowerShell SDK bundled.

## Consequences

- CE-00–03: tab indent, Edit dialog, JSON + PowerShell syntax diagnostics.
- CE-04–07: snippets, remark-lint, PSScriptAnalyzer, polish — documented in CHECKLIST, not bundled in first delivery.
