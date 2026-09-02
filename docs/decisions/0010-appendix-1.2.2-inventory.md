# Appendix: Jotdex 1.2.2 editor inventory (EUX-00)

Baseline: portable **1.2.2** (`v1.2.2` @ `559d061`). This is what exists **before** dialect v2 menus.

## Toolbar (`NoteEditor.tsx`)

| Control | Behavior |
|---|---|
| Pinned / Auto | Chrome pin; localStorage `jotdex.editorChromePinned` |
| H1 H2 H3 | `applyHeadingToSelection` (selection-scoped headings) |
| Bold / Italic / Strike / Code | Tiptap marks |
| Clear | `unsetAllMarks` + `clearNodes` |
| Color / Font size | `JotdexTextStyle` / `JotdexColor` → `<span style>` |
| List / 1. / Todo | bullet, ordered, task list |
| Code box | toggle PowerShell code block |
| Paste code | clipboard → fenced code |
| Link | `window.prompt` then `setLink` |
| Callout select | `setCallout(type)` — type only |
| Table | insert 3×3 with header |
| Attach | file input → upload |
| Undo / Redo | |
| Find | in-note find bar |
| Paste modes | smart / keep HTML / preserve page / plain / as code |

## Shortcuts / plugins

- Wiki link suggest: `[[`
- Heading fold (`headingFold`)
- Block gap cursor between stacked code boxes / images (`blockGapNavigation.ts`) — **keep**
- Shift-Enter split (`ConsistentLineBreaks`)
- Code-block tab indent + plain paste plugin
- Autosave + per-note history (existing coordinators)

## Schema nodes / marks (production extensions)

From `createEditorExtensions.ts`: StarterKit (no built-in codeBlock/link), CodeBlockLowlight, Link, JotdexBlockImage, pendingAsset, AttachmentResolver, JotdexCallout (`type` only), HeadingFold, WikiLinkSuggest, TaskList/TaskItem, Table (resizable), JotdexTextStyle/Color, JotdexTaskMetadata, raw HTML comments, UnresolvedWikiLink, HtmlCommentParse, BlockGapNavigation, official Markdown.

**Not present in 1.2.2:** highlight, underline, sub, sup, details, figure, bookmark card, math, emoji suggestion, typography, drag handle, slash, bubble menu, live outline plugin.

## Paste / export / search

- Paste sessions: `PasteSessionManager` — images download into `Note.assets`; pending placeholders save-blocked.
- Share / static export: Markdig + `ExportCalloutCss` (Obsidian alerts + older `data-callout`).
- Search: `NoteTextExtractor` — YAML strip; does not yet index figure captions / math source / details as first-class fields.
- Outline rail: `extractOutline` string scrape of ATX headings (`outline.ts`), not a ProseMirror plugin.

## Images

- Block image `![alt](src)` with optional title.
- UI: alt/title/remove/broken. **No** resize, caption, alignment, lightbox, or `<figure>` dialect.

## Callouts

- On disk: `> [!note|tip|info|warning|danger]`
- No title on the marker line in the serializer; no collapsible `+`/`-`; live open state not applicable.

## Tests / E2E

- Vitest `src/Web/src/editor` including `schemaCoverage.test.ts` and `blockGapNavigation.test.ts`
- Playwright: `e2e/auth.setup.ts` (isolated password setup), `official-markdown.spec.ts` (SAVE-01 idle open = no PUT), `editor-reliability.spec.ts`
- Isolated host: `scripts/run-editor-e2e.ps1` (temp SampleVault, never `C:\JotdexVault`)

## Portable / vault copy (EUX-00 freeze)

| Artifact | Location | Notes |
|---|---|---|
| 1.2.2 zip | `artifacts/jotdex-win-x64.zip` (gitignored) | Present at freeze (~95 MB) |
| Vault copy | `C:\JotdexMigration\backup` | From MDM-06; **not** inside the live vault; do not apply migrate to `C:\JotdexVault` |
