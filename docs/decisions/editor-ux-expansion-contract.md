# Jotdex Editor UX Expansion Contract

## Document control

- **Project:** Jotdex - Jot + Index
- **Repository:** `jcline123/jotdex`
- **Implementation baseline:** current `main`, beginning from portable Jotdex 1.2.2 and the existing official Tiptap Markdown architecture
- **Target release:** Jotdex 1.3.0 unless the implementation agent documents a strong reason to use a different minor version
- **Contract status:** implementation-ready
- **Primary objective:** add the requested modern editing capabilities without weakening Jotdex's Markdown-first, file-owned, password-protected design
- **Requested scope:** features 1 through 11 from the editor capability review, plus underline, subscript, superscript, text alignment, mathematics, emoji, and safe typography
- **Excluded from this contract:** word count/statistics, real-time collaboration, comments, tracked changes, AI editing, page-layout editing, DOCX editing, and arbitrary HTML/CSS authoring

---

# 1. Executive directive

Implement a major editor experience upgrade for Jotdex while preserving all existing behavior and the existing Markdown safety model.

This is not permission to replace Tiptap, replace the official Markdown engine, change the canonical vault to JSON, store notes in SQLite, weaken authentication, or bulk rewrite the live vault. The implementation must build on the editor reliability work already present in Jotdex 1.1.24, the official `@tiptap/markdown` migration in 1.2.0, the export corrections in 1.2.1, and the block-gap navigation behavior in 1.2.2.

The finished editor must feel substantially more modern and convenient. Users must be able to insert, format, move, inspect, and reorganize content with fewer trips to the top toolbar. At the same time, every persistent feature must have an intentional on-disk representation, an official Tiptap Markdown parser and serializer path, server-render/export support, search support where applicable, and round-trip tests.

The implementation agent must treat the following statement as the central requirement:

> A feature is not complete merely because it looks correct in Tiptap. It is complete only when the content can be saved to an ordinary Jotdex Markdown file, reopened without semantic loss, rendered in Share HTML/static export, searched where appropriate, recovered through history, and edited through the password-protected application.

---

# 2. Required user-facing features

The final release must include all of the following.

1. A `/` slash-command menu.
2. Block drag handles and block actions.
3. A contextual formatting menu for selected text.
4. Better Markdown-safe table creation and editing controls.
5. A full image inspector, including resize, replace, alt text, caption, alignment, lightbox, path/copy actions, and safe failure behavior.
6. Better link creation and editing, including selected-text URL paste, internal note picking, link popover actions, and a portable bookmark-card option.
7. Persistent collapsible Details sections.
8. Text highlighting.
9. Better callouts, including titles, type changes, and optional collapse behavior.
10. A live, useful outline with active-section tracking, deep links, folding integration, and safe section reordering.
11. A visible block insertion button that builds on the existing 1.2.2 block-gap behavior.
12. Underline formatting.
13. Subscript formatting.
14. Superscript formatting.
15. Text alignment for normal paragraphs and headings.
16. Inline and block mathematics rendered with KaTeX.
17. Emoji insertion and Unicode-safe serialization.
18. Optional, constrained smart typography designed for technical notes.

The final editor should expose these through a coherent interaction model rather than adding an uncontrolled number of permanent toolbar buttons.

---

# 3. Existing Jotdex behavior that must remain intact

The implementation must preserve all current editor and product capabilities, including but not limited to:

- Official `@tiptap/markdown` as the only Markdown engine.
- Exact version pinning across all `@tiptap/*` packages.
- Standard Markdown notes and sibling `.assets` folders as canonical content.
- Front matter preservation.
- Source mode and automatic Source-only protection.
- Atomic saves and optimistic concurrency using ETags.
- Revision-aware autosave.
- Per-note history and rollback.
- Paste sessions and pending image placeholders.
- Attachment inventory updates that do not call `setContent()`.
- Existing code-box copy, paste, snippets, CodeMirror edit dialog, and diagnostics.
- Existing H1-H6 behavior and deterministic partial-heading conversion.
- Existing WikiLink suggestion behavior.
- Existing task-list metadata and the Todos rail.
- Existing callout types and legacy callout parsing.
- Existing text color and font-size support.
- Existing table, image, list, quote, link, and horizontal-rule support.
- Existing Outline and heading-fold behavior until their replacements pass feature parity.
- Existing 1.2.2 ability to place a caret or insert a paragraph between stacked block nodes and after the final block.
- Existing Share HTML and static export behavior.
- Existing search indexing.
- Existing mobile editor operation.
- Password, TOTP, cookie authentication, idle lock, and authorization behavior.
- Portable self-contained Windows build with no Node.js requirement at runtime.

No existing feature may be removed merely because a new menu makes it inconvenient to wire up.

---

# 4. Non-negotiable engineering constraints

## 4.1 Files remain the product

Markdown and ordinary attachment files remain the authoritative content. SQLite may continue to hold indexes, caches, preferences, and application state, but never the only copy of note text or embedded content.

## 4.2 Official Markdown only

Production must continue to use official `@tiptap/markdown`. Do not reintroduce `tiptap-markdown`, a second general Markdown engine, a permanent legacy fallback, or a hidden per-note legacy path.

## 4.3 Exact Tiptap version alignment

All `@tiptap/*` packages must remain pinned to one exact version. For the initial implementation, use the same exact version already used by the repository. Do not combine this feature project with a general Tiptap upgrade. If a required open-source extension does not exist at that exact version, document the incompatibility and either implement the required behavior through Jotdex-owned ProseMirror/Tiptap code or split the version upgrade into a separately reviewed prerequisite.

## 4.4 No silent loss

Unsupported or ambiguous content must remain unchanged or open Source-only with a useful reason. Do not flatten, discard, guess, or silently normalize content that cannot be represented safely.

## 4.5 No automatic live-vault rewrite

New features are opt-in and should not require rewriting old notes. Audit only a copy of the real vault. Do not run migration `apply` against `C:\JotdexVault` or any configured live vault unless Joshua explicitly authorizes that separate action after reviewing the report.

## 4.6 No auth bypass for product tests

End-to-end tests must exercise a password-protected Jotdex instance through normal setup/login endpoints. Do not disable authentication, delete the password, use a hard-coded backdoor, or rely on Development bypass after a password exists.

## 4.7 One mutation path

All string or JSON insertion must use the existing typed helpers in `src/Web/src/editor/operations/contentInsertion.ts` or an intentional extension of that module. Do not scatter raw `setContent()` and ambiguous `insertContent(string)` calls throughout React components.

## 4.8 Preserve editor reliability boundaries

Do not collapse the existing codec, revision coordinator, save coordinator, paste-session manager, attachment resolver, safety validator, or operation metadata back into `NoteEditor.tsx`.

## 4.9 One undoable user action

A user-visible command should normally produce one history event. Resizing an image, moving a block, converting a block, inserting a Details section, changing a callout type, or reordering a section must undo in one step.

## 4.10 No paid/private dependency requirement

The portable application must build from public, redistributable dependencies. Do not make Jotdex depend on Tiptap Cloud, a private npm registry, a paid UI component, a paid paste handler, or a runtime network call to Tiptap.

## 4.11 Offline operation

All editor features must work with Jotdex disconnected from the Internet. KaTeX CSS/assets and any emoji fallback data must be packaged locally. No editor feature may silently fetch fonts, icons, emoji images, scripts, or styles from a CDN.

---

# 5. Product design principles

## 5.1 Modern without becoming opaque

The editor should feel closer to modern document editors, but the file representation must remain understandable. Where standard Markdown exists, use it. Where it does not exist, use a small, documented Jotdex dialect that degrades readably in another Markdown viewer.

## 5.2 Fewer permanent toolbar controls

Do not put every command in the top toolbar. Use:

- Slash menu for insertion.
- Visible plus button for insertion between blocks.
- Bubble menu for selected text.
- Drag-handle menu for block operations.
- Context menus/popovers for tables, images, links, callouts, Details, and math.
- A compact `More formatting` toolbar menu for less-common marks.

The existing toolbar remains available and must not lose current actions.

## 5.3 Keyboard and pointer parity

Every drag-only operation must also have a keyboard/button alternative. Every menu must work by keyboard. Every action must work without precise mouse positioning.

## 5.4 Technical-note safety

Jotdex is heavily used for commands, logs, paths, registry keys, hostnames, ticket notes, and scripts. Never apply smart punctuation, math conversion, emoji replacement, linkification, or rich formatting inside code blocks, inline code, raw HTML/comment nodes, task metadata, link destinations, or other literal regions.

## 5.5 Explicit advanced formatting

Advanced features such as image layout, Details, alignment, math, collapsible callouts, and bookmark cards require an explicit user action. Do not reinterpret existing text merely because it resembles the new syntax until the vault-copy audit and collision tests prove that behavior safe.

---

# 6. Target architecture

## 6.1 Shared editor command registry

Create a single command registry used by the slash menu, plus menu, drag-handle block menu, toolbar, keyboard shortcuts, and future command palette.

Recommended location:

```text
src/Web/src/editor/commands/
  EditorCommand.ts
  EditorCommandContext.ts
  createEditorCommandRegistry.ts
  commandGroups.ts
  commandSearch.ts
  commandExecution.ts
```

Each command descriptor should include at least:

```ts
export type EditorCommandDescriptor = {
  id: string
  label: string
  shortLabel?: string
  description: string
  aliases: string[]
  keywords: string[]
  group: 'text' | 'structure' | 'insert' | 'media' | 'technical' | 'format' | 'block'
  icon: EditorIconId
  shortcut?: string
  markdownImpact: 'none' | 'standard' | 'jotdex-dialect'
  isVisible: (ctx: EditorCommandContext) => boolean
  isEnabled: (ctx: EditorCommandContext) => boolean
  disabledReason?: (ctx: EditorCommandContext) => string | undefined
  execute: (ctx: EditorCommandContext) => boolean | Promise<boolean>
}
```

Requirements:

- Stable command IDs are mandatory.
- Menus must not duplicate command logic.
- Availability must be context-aware.
- Commands must report a disabled reason rather than silently doing nothing.
- Commands that open a dialog may return asynchronously.
- Execution must restore/focus the editor selection correctly.
- Every persistent command must run through a tested transaction and the existing revision/autosave pipeline.
- The registry must expose a deterministic list for tests and documentation.

## 6.2 Shared menu/overlay coordinator

Create a small coordinator for editor overlays so slash, plus, bubble, link, table, image, emoji, math, and block menus do not overlap or fight for focus.

Recommended location:

```text
src/Web/src/editor/menus/
  EditorOverlayProvider.tsx
  overlayState.ts
  selectionBookmark.ts
  menuPositioning.ts
  MenuSurface.tsx
  MenuItem.tsx
```

Rules:

- Only one primary editor menu is open at a time.
- Opening a modal closes transient menus but preserves the intended editor insertion position.
- Pointer-down on a formatting control must not destroy the selection before execution.
- Escape closes the topmost editor surface and returns focus appropriately.
- Scrolling, note switching, Source-mode switching, idle lock, and conflict replacement must close stale overlays.
- Menus must never be rendered into an element hidden behind the idle-lock gate.

## 6.3 Selection bookmarks

Use ProseMirror bookmarks or mapped document positions rather than DOM ranges whenever an asynchronous action may occur. This applies to image replacement, image insertion, link dialogs, math dialogs, emoji menus, table dialogs, attachment upload, and any command that waits for a network/API response.

A selection bookmark must include:

- Note ID.
- Note-session ID.
- Document revision/transaction mapping support.
- Selection type.
- From/to positions or node position.
- An operation ID.

Before applying an async result, confirm the note session still matches. If it does not, discard the UI result without modifying another note.

## 6.4 Jotdex dialect version 2

Extend the current Jotdex Markdown dialect intentionally. Add an ADR that describes every new syntax in this contract.

Recommended ADR:

```text
docs/decisions/0010-editor-ux-and-jotdex-dialect-v2.md
```

The ADR must explain:

- Why each nonstandard syntax is necessary.
- How it appears outside Jotdex.
- How it is parsed and serialized.
- How malformed forms are handled.
- Which old forms are accepted.
- Whether canonicalization occurs only after an actual user edit.
- Search and export behavior.
- Security and sanitizer behavior.

## 6.5 Persistent-node coverage gate

Extend the existing schema-coverage test. Every persistent node and mark in the editor schema must have one of the following:

1. An official Markdown parser and renderer.
2. A Jotdex-owned parser and renderer.
3. A documented, tested Source-only rule that prevents visual save.
4. A clearly nonpersistent/editor-only classification.

The test must fail when a developer adds a node or mark without declaring one of those outcomes.

## 6.6 Rendering parity

For every new persistent feature, update all relevant representations:

- Visual editor.
- Source editor.
- Server Markdown HTML rendering.
- Share HTML export.
- Static site export.
- Search text extraction.
- History/diff preview.
- Formatting checker where appropriate.
- AI prompt/help text describing the supported Jotdex Markdown dialect.

## 6.7 Test fixture strategy

Add a new fixture family:

```text
tests/RoundTripFixtures/editor-ux-v2/
```

Include one focused fixture per syntax plus one combined stress document. The combined document must include all new content types next to images, headings, lists, code blocks, tables, and task metadata.

---

# 7. Dependency and license policy

The agent must verify package existence, license, and exact-version compatibility before installation. Expected open-source packages include, subject to exact-version verification:

```text
@tiptap/extension-drag-handle
@tiptap/extension-details
@tiptap/extension-highlight
@tiptap/extension-underline
@tiptap/extension-subscript
@tiptap/extension-superscript
@tiptap/extension-text-align
@tiptap/extension-mathematics
@tiptap/extension-emoji
@tiptap/extension-typography
@tiptap/suggestion
@floating-ui/dom
katex
```

Use the React `BubbleMenu` and `FloatingMenu` components already available through `@tiptap/react/menus` when appropriate rather than adding duplicate extensions.

Do not install a paid Tiptap UI component merely to avoid writing Jotdex-specific markup. Do not install the paid Paste Handler. Jotdex already owns a paste pipeline and must continue to do so.

Update:

- `src/Web/package.json`
- `src/Web/package-lock.json`
- `THIRD_PARTY_NOTICES.md`
- Any license inventory/tests
- Portable publishing inputs

All added `@tiptap/*` versions must exactly equal the rest of Tiptap. Non-Tiptap packages may use the repository's normal version policy, but the lockfile must be committed.

---

# 8. Canonical on-disk formats

The following formats are the required default unless an implementation spike proves a severe incompatibility and the agent updates the ADR before proceeding.

## 8.1 Highlight

```markdown
This is ==important text==.
```

Rules:

- One default highlight color in the first release.
- Do not persist arbitrary highlight colors.
- Accept `<mark>` on HTML paste and normalize to `==...==` after a real edit.

## 8.2 Underline

```html
This is <u>underlined</u>.
```

## 8.3 Subscript

```html
H<sub>2</sub>O
```

## 8.4 Superscript

```html
x<sup>2</sup>
```

## 8.5 Text alignment

Use an ignorable metadata comment immediately before the affected top-level paragraph or heading:

```markdown
<!-- jotdex-align: center -->
## Centered heading
```

```markdown
<!-- jotdex-align: right -->
This paragraph is right aligned in Jotdex.
```

Rules:

- Allowed values: `center`, `right`, `justify`.
- Left alignment is the absence of the marker.
- The marker applies to exactly the next top-level paragraph or heading.
- Do not support alignment inside code blocks, tables, lists, blockquotes, callouts, Details content, or raw HTML in the first release.
- Moving or deleting the aligned block must move/delete the metadata as part of the same semantic block.
- Source mode exposes the marker normally.
- Another Markdown viewer ignores the comment and still shows the content.

## 8.6 Inline mathematics

Use explicit TeX delimiters that do not collide with ordinary currency:

```markdown
The result is \(x^2 + y^2\).
```

## 8.7 Block mathematics

```markdown
\[
\frac{a}{b}
\]
```

Rules:

- Do not register the stock `$...$` tokenizer if it would reinterpret existing currency or PowerShell text.
- Extend or wrap Tiptap's mathematics nodes with Jotdex-owned Markdown tokenizers/renderers for `\(...\)` and `\[...\]`.
- Accept `$...$` and `$$...$$` only through an explicit import/conversion command or a future reviewed compatibility option; do not silently convert existing notes.
- Preserve LaTeX source exactly except newline normalization.

## 8.8 Emoji

Store the actual Unicode character:

```markdown
Deployment complete ✅
```

Do not store shortcode-only forms, remote image URLs, or package-specific IDs as canonical note content.

## 8.9 Details sections

Use a Jotdex marker surrounding otherwise normal Markdown. The visible summary is a normal bold paragraph so non-Jotdex viewers still show meaningful content:

```markdown
<!-- jotdex-details -->
**Advanced troubleshooting**

These steps are collapsed in Jotdex but remain ordinary Markdown outside it.

- Step one
- Step two

<!-- /jotdex-details -->
```

Rules:

- Use Tiptap Details/DetailsSummary/DetailsContent nodes visually.
- Configure open-state persistence off. Temporary open/closed state must not autosave.
- The first content block after the opening marker is the summary.
- The summary must be a single text block. It may contain supported inline marks but not images, links to unsafe schemes, math blocks, or nested blocks.
- Nested Details are not supported in the first release.
- Missing/duplicate/misordered markers force Source-only with a useful reason.
- Import standard `<details><summary>...</summary>...</details>` when it can be converted without loss, but do not bulk rewrite existing files.

## 8.10 Callouts

Existing forms remain valid:

```markdown
> [!warning]
> Warning content.
```

Add titles:

```markdown
> [!warning] Client impact
> Users will need to sign in again.
```

Add optional collapse defaults using the common `+`/`-` convention:

```markdown
> [!warning]- Client impact
> This callout is collapsible and initially collapsed.
```

```markdown
> [!tip]+ Verification steps
> This callout is collapsible and initially expanded.
```

Rules:

- No suffix means a normal noncollapsible callout.
- `-` means collapsible and initially collapsed.
- `+` means collapsible and initially expanded.
- Live expansion/collapse alone does not dirty the document.
- Changing the default collapse mode is a real edit.
- Existing titleless callouts must continue to parse and serialize.

## 8.11 Standard image

Keep ordinary images ordinary:

```markdown
![Alt text](Note%20Name.assets/image.png "Optional title")
```

## 8.12 Advanced image figure

Only images that use a caption, custom width, or nondefault alignment become a Jotdex figure:

```html
<figure data-jotdex-image="1" data-width="65" data-align="center">
  <img src="Note%20Name.assets/image.png" alt="Firewall error" title="Optional title">
  <figcaption>Screenshot after applying the policy</figcaption>
</figure>
```

Rules:

- `data-width` is an integer percentage from 20 through 100.
- Omit width at 100.
- Alignment values: `left`, `center`, `right`.
- Omit alignment when left/default.
- Caption is plain text in the first release. Escape HTML characters.
- The image `src` must remain the canonical vault-relative path, never `/api/attachments/...`, `blob:`, `data:`, or a pending URL.
- A figure with no advanced attributes should serialize back to ordinary Markdown image syntax.
- Sanitizers must allow only the exact required tags/attributes in this shape.

## 8.13 Bookmark card

Use a marker plus an ordinary link so the note remains readable elsewhere:

```markdown
<!-- jotdex-link-card -->
[Microsoft Conditional Access](https://learn.microsoft.com/example)
```

Rules:

- The title and destination are canonical; remote description/favicon are cache only.
- If cached metadata is missing or Jotdex is offline, render a useful card from the link text and host.
- Another Markdown viewer ignores the marker and shows the ordinary link.
- Malformed markers remain Source-visible and must not consume unrelated content.

---

# 9. Source-mode and migration policy

Opening an existing note must not rewrite it. The editor may internally parse old compatible forms, but no canonicalization reaches disk until the user makes a real semantic edit.

Before enabling 1.3.0 by default, audit a copy of the real vault for collisions with:

- `==highlight==`
- `<u>`, `<sub>`, `<sup>`
- `<!-- jotdex-align: ... -->`
- `<!-- jotdex-details -->` and closing markers
- `\(...\)` and `\[...\]`
- `<figure data-jotdex-image>`
- `<!-- jotdex-link-card -->`
- Titled/collapsible callout marker lines

The audit must report:

- Total notes.
- Notes containing each new syntax.
- Notes that parse safely.
- Notes that would change semantic fingerprints.
- Notes requiring Source-only mode.
- Ambiguous dollar/math-like text even though dollar math is not enabled.
- Raw HTML conflicts.
- Broken markers.
- Missing assets and unsafe URLs.

Do not apply cleanup to the live vault as part of this contract. If cleanup is later approved, use `audit -> stage -> verify -> apply -> rollback` through a migration tool with hashes and reports.

---

# 10. Feature 1 - slash-command menu

## 10.1 User experience

Typing `/` at the beginning of an empty paragraph or after leading whitespace must open a searchable command menu at the caret.

Initial categories:

### Text and structure

- Text
- Heading 1
- Heading 2
- Heading 3
- Heading 4
- Heading 5
- Heading 6
- Bullet list
- Numbered list
- Checklist
- Quote
- Horizontal line

### Technical

- Code box
- Inline code when a text selection exists
- Insert snippet
- Inline math
- Math block

### Insert

- Table
- Callout: Note
- Callout: Tip
- Callout: Info
- Callout: Warning
- Callout: Danger
- Details section
- Image
- Attachment
- Link
- Link to note
- Bookmark card
- Emoji
- Template

### Formatting shortcuts

- Highlight selection
- Underline selection
- Subscript selection
- Superscript selection
- Align left
- Align center
- Align right
- Justify
- Clear formatting

## 10.2 Trigger rules

The slash menu must open only when:

- The editor is editable.
- Visual mode is active.
- The caret is in a paragraph or heading context where insertion is valid.
- `/` begins the current text query after paragraph start or leading whitespace.
- The selection is a caret unless a command explicitly supports a selection.

It must not trigger inside:

- Code blocks.
- Inline code.
- Inline/block math.
- Raw HTML/comment nodes.
- Task metadata atoms.
- Link destinations.
- Image/figure captions while a node dialog is active.
- Source mode.
- Read-only mode.
- An active IME composition.

## 10.3 Query and ranking

- Search command label, aliases, keywords, and description.
- Use case-insensitive fuzzy matching.
- Exact prefix matches rank first.
- Recently used commands may receive a small local preference boost but may not alter deterministic unit tests unless the recency store is injected.
- Show at most 12 results before scrolling.
- Empty query shows a deliberate default order rather than alphabetical noise.
- Commands unavailable in the current context may either be hidden or shown disabled with a concise reason; choose consistently by command class.

Examples:

```text
/code       -> Code box
/warn       -> Warning callout
/check      -> Checklist
/pic        -> Image
/math       -> Inline math, Math block
/note       -> Link to note, Note callout
```

## 10.4 Keyboard behavior

- `ArrowDown` and `ArrowUp` move through results.
- `Home` and `End` jump to first/last result.
- `PageDown` and `PageUp` move by a visible page when the menu scrolls.
- `Enter` executes the active command.
- `Tab` executes the active command.
- `Escape` closes the menu and leaves the typed `/query` unchanged.
- `Backspace` closes the menu when the trigger is deleted.
- Clicking elsewhere closes it without stealing editor text.
- After command execution, focus returns to the correct editor position or the newly opened modal.

## 10.5 Transaction behavior

For commands that replace the slash text:

1. Capture the trigger range.
2. Delete only the trigger and query.
3. Execute the command at the mapped range.
4. Set operation metadata such as `kind: 'slash-command'` and the command ID.
5. Add the complete insertion to history as one user action.
6. Let the normal revision coordinator serialize after the transaction settles.

Commands that upload an image or attachment must insert a stable placeholder immediately and use the existing paste/asset session machinery.

## 10.6 Implementation guidance

Tiptap does not provide a completed official slash-command extension. Build a Jotdex-owned implementation using `@tiptap/suggestion` if the exact pinned version is compatible, or a small ProseMirror plugin if it is not. Do not copy a paid Tiptap template with licensing/runtime dependencies.

Recommended files:

```text
src/Web/src/editor/slash/
  SlashCommandExtension.ts
  SlashCommandMenu.tsx
  slashQuery.ts
  slashState.ts
  slashCommands.test.ts
```

The menu must consume the shared command registry. It must not define separate command callbacks.

## 10.7 Acceptance criteria

- Typing `/` on an empty line opens the menu in Chromium, Firefox, and WebKit.
- `/code` followed by Enter creates exactly one code block and removes the query.
- `/warning` creates the existing Jotdex callout type and round-trips.
- `/image` inserts at the original position even if upload completion is delayed.
- The menu never opens inside a code block.
- Escape does not delete the user's query.
- Undo restores the exact text and structure present before command execution.
- Menu navigation is fully keyboard accessible.
- Command execution does not directly save or bypass validation.

---

# 11. Feature 2 - block drag handles and block actions

## 11.1 User experience

Hovering near a top-level block must reveal a drag handle in the editor's left gutter. On touch devices, tapping the block gutter or a block-actions button must reveal equivalent controls.

The handle should visually identify the current block without changing the document selection. It must work for:

- Paragraphs.
- Headings.
- Bullet, numbered, and task lists as whole top-level list blocks.
- Blockquotes.
- Code blocks.
- Tables as whole blocks.
- Standard images.
- Advanced image figures.
- Callouts.
- Details sections.
- Block math.
- Horizontal rules.

The initial release must target top-level blocks only. Nested list-item dragging may be added only after all top-level behavior passes and a separate nested test matrix is implemented.

## 11.2 Drag behavior

- Dragging moves the complete selected block.
- Show a clear drop cursor.
- Preserve the block's internal structure and marks.
- Keep hidden metadata logically attached to its block, including text-alignment comments and link-card markers.
- Tables move as one table, not a row or cell.
- Callouts and Details move as one container.
- Advanced image figures move with caption and attributes.
- Upload placeholders may move while uploading because resolution uses stable upload IDs; test this explicitly.
- A failed or canceled drag makes no document change.
- Dragging outside valid drop zones must not delete content.
- Dragging across the editor's top/bottom should scroll the editor at a controlled rate.
- Moving a block must be a single undo operation.

## 11.3 Block actions menu

Clicking the handle must lock it and open a context menu with context-appropriate actions:

- Turn into Text
- Turn into Heading 1-6
- Turn into Bullet list
- Turn into Numbered list
- Turn into Checklist
- Turn into Quote
- Turn into Code box
- Turn into Callout
- Move up
- Move down
- Duplicate
- Copy
- Cut
- Add block above
- Add block below
- Delete

Context-specific actions:

- Image/figure: Open image inspector, Replace, Copy path, Remove.
- Table: Open table controls, Delete table.
- Callout: Change type, Edit title, Collapse settings, Unwrap.
- Details: Edit summary, Unwrap.
- Math: Edit formula, Convert to source text, Delete.
- Heading: Copy deep link; Move entire section from Outline.

Do not show transformations that cannot preserve content. For example, a table cannot become a heading; show no such option rather than flattening it.

## 11.4 Keyboard alternatives

Dragging cannot be the only way to reorder content.

Required actions:

- Move block up.
- Move block down.
- Move block to start.
- Move block to end.
- Open block actions via keyboard when focus is in the block.
- Announce the result through an ARIA live region, such as `Moved code block above Heading 2`.

Do not assign shortcuts that conflict with browser, operating system, or existing Jotdex shortcuts. Document any new shortcuts in the Help/AI prompt.

## 11.5 Dependency/implementation rules

Prefer the open-source `@tiptap/extension-drag-handle` at the exact pinned Tiptap version after license and availability verification. Use Jotdex-owned React/DOM markup for the handle and menu. Do not depend on a paid Drag Context Menu component.

Recommended files:

```text
src/Web/src/editor/blocks/
  JotdexDragHandle.ts
  BlockHandleView.tsx
  BlockActionsMenu.tsx
  blockTarget.ts
  blockMove.ts
  blockTransform.ts
  blockClipboard.ts
  blockActions.test.ts
```

The implementation must cooperate with:

- The existing heading-fold decorations.
- The existing 1.2.2 block-gap navigation plugin.
- Selection bubble menus.
- Table and image node views.
- Editor toolbar auto-hide.
- Pop-out note windows.
- Mobile panes.

## 11.6 Safety rules

- Never target hidden metadata nodes independently.
- Never split task metadata away from its list item.
- Never move only half of a table.
- Never allow a top-level block to be dropped into a code block or inline atom.
- Do not allow a block to be dropped into itself or its own descendant.
- Do not serialize during pointer-move events.
- Do not create one history entry per mouse movement.
- Only dispatch the final move transaction on a valid drop.

## 11.7 Acceptance criteria

- Every supported top-level block can be moved above and below another block.
- Metadata remains attached.
- The Markdown order after save matches the visual order.
- Undo/redo restores the exact order.
- Upload completion still resolves a placeholder after it was moved.
- Keyboard Move up/down provides feature parity with dragging.
- Dragging does not open the slash, bubble, plus, link, or image menus unexpectedly.
- Dragging a folded heading does not silently move hidden section content; the normal block handle moves only the heading. Section movement is handled by the Outline feature.

---

# 12. Feature 3 - contextual selection formatting menu

## 12.1 User experience

Selecting normal text must show a compact bubble menu near the selection.

Primary row:

```text
Bold | Italic | Underline | Strike | Code | Highlight | Link | More
```

`More` opens:

```text
Text color
Font size
Subscript
Superscript
Clear formatting
```

When the selection is entirely within one paragraph or heading, a secondary block control may offer:

```text
Text | H1 | H2 | H3 | Quote | Alignment
```

Do not duplicate the full top toolbar in a giant floating panel.

## 12.2 Show/hide rules

Show the text bubble menu only when:

- Visual mode is active.
- The editor is editable.
- There is a nonempty text selection.
- The selection is in supported prose content.
- The browser is not actively composing an IME sequence.

Hide it for:

- Node selections.
- Code blocks.
- Block math.
- Raw HTML/comment nodes.
- Pending asset placeholders.
- Whole-table/cell selections where the table menu is more appropriate.
- Source mode.
- Read-only mode.
- Idle lock.
- A selection spanning incompatible structures.

Inline code may be applied to prose text, but when the selection already resides inside inline code, do not show controls that would create unsafe overlapping marks unless the serializer suite proves them safe.

## 12.3 Selection preservation

Use the React `BubbleMenu` from `@tiptap/react/menus` with an explicit plugin key. On pointer down, preserve the editor selection. Button focus must not collapse the selection before the command runs.

For dropdowns and dialogs:

- Capture a mapped selection bookmark.
- Open the surface.
- Restore the selection before applying the command.
- Abort safely if the note session changed.

## 12.4 Active and mixed state

Each control must display:

- Active when the entire selection has the mark.
- Inactive when none has it.
- Mixed when only part has it.
- Disabled when invalid in context, with an accessible explanation.

Do not infer state solely from the caret at one end of a range.

## 12.5 Clear formatting

`Clear formatting` must:

- Remove bold, italic, underline, strike, highlight, inline code, color, font size, subscript, and superscript from the selection.
- Preserve links unless the user chooses Remove link separately.
- Preserve task metadata and other structural nodes.
- Preserve normal paragraph/heading structure unless `Clear block formatting` is explicitly selected.
- Avoid changing text content or whitespace.

## 12.6 Accessibility and mobile

- Use a toolbar role and meaningful button labels.
- Expose `aria-pressed` for toggle marks.
- Keep controls reachable by Tab and arrow keys.
- On narrow screens, position the menu within the viewport and permit horizontal scrolling or a compact `More` menu.
- Touch targets must be at least 44 by 44 CSS pixels when displayed in mobile mode.
- Escape closes the menu and returns focus to the selection.

## 12.7 Acceptance criteria

- Selecting text and choosing Bold changes only the selection.
- Underline, highlight, subscript, and superscript survive save/reopen.
- Mixed-state formatting is displayed correctly.
- Opening a color/size submenu does not lose the selection.
- Bubble menu never appears inside code boxes.
- Undo reverses each command in one step.
- The bubble menu causes no Markdown serialization on selection-only transactions.

---

# 13. Feature 4 - Markdown-safe table editing

## 13.1 Product goal

Make tables practical to create and maintain without exposing table structures that ordinary GFM Markdown cannot store safely.

The table experience must include creation, row/column changes, header management, deletion, paste support, navigation, and clear constraints. Do not advertise HTML-table capabilities that Jotdex will later flatten or force Source-only.

## 13.2 Table insertion

`Table` from the slash/plus menu must open a compact table builder.

Required controls:

- Rows: default 3, minimum 1, maximum 50.
- Columns: default 3, minimum 1, maximum 20.
- `First row is header`: default on.
- Optional preview grid for up to 10 by 10 quick selection.
- Insert and Cancel.

Insertion must:

- Replace an empty host paragraph when appropriate rather than leaving an unexplained empty block.
- Insert a valid table followed by a usable paragraph/caret location.
- Select the first editable cell.
- Produce one undo step.
- Serialize to a valid GFM table immediately.

## 13.3 Contextual table controls

When the caret is inside a table, show a small table toolbar or popover with:

- Add row above.
- Add row below.
- Delete row.
- Add column left.
- Add column right.
- Delete column.
- Move row up.
- Move row down.
- Move column left.
- Move column right.
- Toggle first row as header.
- Set current column alignment: left, center, right.
- Delete table.
- Open full table menu.

Use Tiptap's existing table commands where they preserve GFM semantics. Implement row/column move operations as reviewed ProseMirror transactions if not provided by Tiptap.

## 13.4 Explicitly unsupported table controls

Do not enable the following in 1.3.0:

- Merged cells.
- Split cells.
- Rowspan or colspan.
- Multiple paragraphs/blocks in one cell.
- Nested tables.
- Images, code blocks, callouts, Details, or lists inside cells.
- Arbitrary cell background colors.
- Per-cell borders or custom CSS.
- Header columns that cannot be represented consistently by GFM.

If Tiptap exposes these commands, do not surface them. Tests must also prevent accidental keyboard actions from creating unsupported shapes.

## 13.5 Cell editing behavior

Each table cell should remain one paragraph with inline content.

- `Enter` inside a table cell should move to the next logical cell or insert a Markdown-safe hard break according to a documented rule; it must not create a second block in the cell.
- `Shift+Enter` may insert a hard break rendered as `<br>` if the official serializer round-trips it.
- `Tab` moves forward.
- `Shift+Tab` moves backward.
- Tab from the final cell may add a new row only when the table is editable and the resulting table remains valid.
- `Mod+Enter` may exit the table into a paragraph below.
- Pasting multiple lines into a cell must either create safe `<br>` breaks or ask whether to distribute data into cells; it must not create hidden multi-block content.

The exact key behavior must be documented and consistent across browsers.

## 13.6 Column alignment storage

Prefer standard GFM delimiter alignment:

```markdown
| Left | Center | Right |
| :--- | :----: | ----: |
| A    | B      | C     |
```

If official `@tiptap/markdown` 3.29.2 does not preserve alignment attributes, add Jotdex-owned table parse/render handlers. Do not fall back to HTML tables merely for alignment.

Alignment applies by column, not one random cell. The table UI must communicate this.

## 13.7 Paste from spreadsheets

Extend the existing paste cleaner rather than adding the paid Tiptap Paste Handler.

Supported sources should include best-effort HTML/clipboard tables from:

- Microsoft Excel desktop/web.
- Google Sheets web.
- HTML tables copied from a browser.

Required behavior:

1. Sanitize the HTML.
2. Detect a rectangular table.
3. Preserve plain text, inline marks, links, and safe hard breaks.
4. Reject or flatten unsupported nested blocks explicitly.
5. Ask whether the first row is a header when the source does not make that clear.
6. Insert one valid Tiptap table transaction.
7. Serialize and validate before allowing autosave.

If the clipboard contains tab/newline-separated plain text, offer `Paste as table` when at least two rows and two columns are detected.

## 13.8 Table validation

After every structural table command:

- Run Tiptap/ProseMirror table repair if needed.
- Verify rectangular shape.
- Verify exactly one block per cell.
- Verify no control characters.
- Verify a Markdown header row exists when required by the serializer.
- Serialize through `OfficialMarkdownCodec` in tests.
- Refuse save if the document becomes unsafe.

## 13.9 Search/export behavior

- Search must index all visible cell text in row order.
- Share HTML and static export must render header rows, alignment, wrapping, and horizontal overflow cleanly.
- Mobile rendering must permit horizontal scrolling without forcing the whole page wider.
- Table controls are editor-only and never appear in exports.

## 13.10 Suggested files

```text
src/Web/src/editor/tables/
  TableInsertDialog.tsx
  TableBubbleMenu.tsx
  tableCommands.ts
  tableMove.ts
  tablePaste.ts
  tableValidation.ts
  tableMarkdown.ts
  tableEditing.test.ts
```

Extend the existing `tableCompatibility.ts` rather than creating a second contradictory validator.

## 13.11 Acceptance criteria

- User can insert, add, remove, and reorder rows/columns.
- Header-row toggle round-trips.
- Column alignment round-trips in GFM.
- No surfaced command can create merged or multi-block cells.
- Pasted 5 by 5 spreadsheet data becomes one valid table.
- Undo reverses each structural operation in one step.
- Saving/reopening preserves every cell and alignment.
- Share/static export matches the editor semantically.
- Table controls work in Chromium, Firefox, WebKit, and narrow mobile viewport tests.

---

# 14. Feature 5 - full image inspector

## 14.1 Product goal

Turn the current image node from a mostly visual attachment with Remove into a complete, reliable media workflow without destabilizing paste sessions or storing runtime attachment URLs in Markdown.

## 14.2 Image selection experience

Clicking a standard image or advanced image figure must select it and show a contextual image inspector. The inspector may be a bubble menu plus an expanded dialog/side panel.

Quick actions:

- Open full size.
- Replace.
- Edit alt text.
- Edit caption.
- Width presets: 25%, 50%, 75%, 100%.
- Align left, center, right.
- Copy image.
- Copy canonical path.
- Download/open attachment.
- Reset layout.
- Remove.

Expanded details:

- File name.
- Content type.
- File size when available.
- Canonical relative path.
- Broken/missing status.
- Alt text.
- Optional title.
- Caption.
- Width percentage.
- Alignment.

## 14.3 Standard image versus figure

A normal image with no caption, no custom width, and left/default alignment remains a standard Markdown image node.

When the user adds a caption, changes width below 100%, or chooses center/right alignment:

- Convert the image to the Jotdex figure node.
- Preserve `src`, alt, title, selection, and attachment identity.
- Serialize using the canonical figure HTML in section 8.12.

When all advanced attributes are reset:

- Convert back to the standard image node.
- Serialize as normal Markdown.

This prevents every simple pasted screenshot from becoming custom HTML.

## 14.4 Resizing

Provide drag-to-resize handles on selected figures/images.

Requirements:

- During pointer movement, apply a view-only preview style.
- Dispatch one final document transaction on pointer release.
- Store integer width percentage, not unstable viewport pixels.
- Clamp from 20 through 100.
- Preserve aspect ratio.
- Provide keyboard resize alternatives in 5% increments.
- Escape cancels an in-progress resize and restores the original width.
- Resizing must create one undo event.
- Resizing must not upload, recompress, or rewrite the binary image.

## 14.5 Replace image

Replacing an image must use the existing attachment upload path and note-session safeguards.

Flow:

1. Capture the selected node position using a mapped bookmark and stable operation ID.
2. Open file picker.
3. Insert or show replacement progress without deleting the current image.
4. Upload to the current note's assets directory.
5. Confirm note session and target node still exist.
6. Replace only the image source/attachment metadata after successful upload.
7. Preserve alt, title, caption, width, and alignment unless the user chooses to reset them.
8. On failure, leave the old image untouched and display Retry/Cancel.

Do not automatically delete the old asset because another note, HTML sidecar, history version, or duplicate image could still reference it. Asset cleanup is a separate feature.

## 14.6 Alt text and caption rules

- Alt text is required for informative images but may be empty when the user marks an image decorative.
- Display a nonblocking accessibility warning for empty alt text unless decorative.
- Caption is plain text in 1.3.0.
- Caption may include normal punctuation and Unicode but no nested blocks or arbitrary HTML.
- Alt/title/caption edits must escape correctly in Markdown/HTML.
- New pasted screenshots may default alt text to the filename but should invite editing.

## 14.7 Lightbox and full-size view

- Clicking `Open full size` opens a modal/lightbox using the authenticated attachment URL.
- Keep the canonical Markdown `src` unchanged.
- Support zoom, fit-to-window, original size, and close by Escape.
- Do not expose the underlying filesystem path.
- Respect authentication and attachment MIME policies.
- Broken images show a useful error rather than an empty modal.

## 14.8 Copy and download

- `Copy image` should place image bytes on the clipboard when the browser permits, with a graceful fallback to copying the authenticated URL or path.
- `Copy canonical path` copies the vault-relative Markdown target.
- `Download` uses the normal authenticated attachment endpoint with safe content disposition.
- Never copy `blob:` or pending placeholder URLs as the canonical path.

## 14.9 Broken images

Retain the existing broken-image treatment and improve it:

- Show file label/path.
- Offer Locate/Replace if appropriate.
- Offer Remove.
- Do not alter surrounding text.
- A failed image load must not dirty the note.
- Missing assets should appear in integrity diagnostics.

## 14.10 Server/export/search changes

- Allow and sanitize the exact Jotdex figure shape.
- Rewrite relative figure image paths to authenticated URLs in live HTML preview.
- Copy figure assets into static export.
- Render caption and alignment in Share/static HTML.
- Search index alt text, title, caption, and file name.
- History diff should show meaningful figure changes rather than an unreadable single-line HTML blob where practical.

## 14.11 Suggested files

```text
src/Web/src/editor/images/
  JotdexFigureImage.ts
  FigureImageView.tsx
  ImageBubbleMenu.tsx
  ImageInspectorDialog.tsx
  imageSelectionBookmark.ts
  imageReplace.ts
  imageResize.ts
  imageMarkdown.ts
  imageInspector.test.ts
```

Refactor the current `ImageView.tsx` rather than leaving two unrelated image node views.

## 14.12 Acceptance criteria

- Simple images remain standard Markdown.
- Caption/resize/alignment upgrades to figure and round-trips.
- Reset downgrades to standard Markdown without loss.
- Replace failure leaves the original image intact.
- Image can be moved while an upload is pending and still resolves correctly.
- Width/alignment/caption survive save, reopen, Share HTML, and static export.
- No runtime attachment URL is written to the note.
- Each inspector change is undoable.
- Lightbox and controls work with password authentication enabled.

---

# 15. Feature 6 - better links and bookmark cards

## 15.1 Link creation

Support the following workflows:

### Selected text plus pasted URL

When normal text is selected and the clipboard contains one valid URL:

- Paste should create a link around the selection rather than replace the selected text.
- Do not do this inside code, math, raw HTML, or an existing link unless explicitly editing it.
- If the clipboard contains more than one line or mixed content, use normal paste behavior.

### Link dialog

A Link command opens a dialog with:

- Display text.
- Destination.
- `Open in new tab` is a view behavior, not required Markdown metadata.
- Test/Open button.
- Save.
- Remove link when editing.

### Link to note

Use the current note catalog to search by title/path/tags and insert a relative `.md` link. Reuse the same relative-path function used by WikiLink resolution. Do not store an application-only note ID in the Markdown destination.

## 15.2 Link popover

When the caret is inside a link, show a link-specific popover:

- Open.
- Edit.
- Copy address.
- Copy Markdown.
- Remove link.
- Convert standalone external link to bookmark card.
- For internal note links: Open note and Show in folder.

The popover must not replace the normal text-selection bubble menu. Use separate plugin keys and explicit precedence.

## 15.3 URL validation

Allow only intentional schemes:

- `https:`
- `http:`
- `mailto:`
- `tel:` where useful
- Relative Markdown/file paths that stay within the vault's logical link model

Reject or treat as literal text:

- `javascript:`
- `data:`
- `vbscript:`
- `file:`
- UNC paths in clickable browser links
- Device paths
- Control-character or whitespace-obfuscated schemes

The user may still document these strings inside code blocks or inline code.

## 15.4 URL paste on an empty line

When a single external URL is pasted into an otherwise empty paragraph, show a small choice:

- Paste as link.
- Paste as bookmark card.
- Clip page.
- Paste as plain text.

Do not block normal paste while waiting. A reasonable implementation is to insert a normal link immediately and show a nonmodal conversion prompt that can be dismissed.

## 15.5 Bookmark-card behavior

A bookmark card is a Jotdex visual node serialized as the portable marker plus ordinary link in section 8.13.

Visual card fields:

- Link title.
- Host/domain.
- Optional cached description.
- Optional locally cached icon only if explicitly fetched and stored safely.
- Open button.
- Edit link.
- Convert back to normal link.

Canonical rules:

- Only title and URL are required in the note.
- Description/favicon metadata lives in rebuildable application cache, not as the only copy of meaningful note text.
- Fetch metadata only after an explicit user action or link-card creation.
- Use the existing SSRF-safe remote page client rules: HTTP/HTTPS only, redirect/size/time limits, DNS/IP revalidation, and blocking loopback/private/link-local destinations unless explicitly allowed by existing policy.
- Failure to fetch metadata still produces a usable offline card.
- Do not auto-refresh every time the note opens.

## 15.6 Markdown behavior

- Standard links remain standard Markdown.
- Do not unnecessarily rewrite autolinks into labeled links or vice versa.
- Preserve titles when present.
- Escape parentheses, spaces, and special characters consistently.
- Internal note link rewrites during note move/rename must continue to work.
- Link-card metadata must travel with the link during block drag, duplicate, cut/paste, and section reorder.

## 15.7 Accessibility

- Link text must be editable separately from URL.
- Popover controls need accessible names.
- The card itself must have a clear primary link and must not become one inaccessible nested button.
- Keyboard users can open, edit, convert, and remove links.
- Show the destination host to reduce deceptive-link risk.

## 15.8 Suggested files

```text
src/Web/src/editor/links/
  LinkBubbleMenu.tsx
  LinkDialog.tsx
  linkValidation.ts
  selectedTextUrlPaste.ts
  InternalNotePicker.tsx
  JotdexLinkCard.ts
  LinkCardView.tsx
  linkCardMarkdown.ts
  linkEditing.test.ts
```

## 15.9 Acceptance criteria

- Pasting a valid URL over selected text creates a link and preserves the text.
- Editing link text/URL round-trips.
- Unsafe schemes are rejected.
- Internal links remain portable relative Markdown links.
- Bookmark cards remain readable as links outside Jotdex.
- Bookmark metadata failure does not block save or opening.
- Link popover works without collapsing the selection.
- Link-card marker remains attached through drag and section moves.

---

# 16. Feature 7 - persistent collapsible Details sections

## 16.1 Product goal

Add intentional collapsible content that is part of the note, distinct from the existing editor-only heading folding.

Examples:

- Full command output.
- Advanced troubleshooting.
- Prior configuration.
- Vendor response.
- Optional background information.

## 16.2 Visual structure

Use Tiptap's Details, DetailsSummary, and DetailsContent nodes, extended where necessary for official Jotdex Markdown support.

A Details block contains:

- One required summary text block.
- One or more normal content blocks.
- A toggle control with an accessible expanded/collapsed state.
- A block handle that moves the entire Details block.
- A context menu: Edit summary, Open/Close, Unwrap, Duplicate, Delete.

## 16.3 Persisted versus temporary state

Configure Details open-state persistence off.

- Clicking the disclosure arrow is a view action and does not dirty/autosave.
- Creating a Details section, editing its summary/body, moving it, or unwrapping it is a document edit.
- The app may remember open sections for the current note/session in ephemeral UI state, but that state must not enter Markdown or interfere with semantic comparisons.

## 16.4 Insertion and conversion

Slash/plus command `Details section` should:

- Replace an empty paragraph or wrap a compatible selected range.
- Prompt for or create a default summary such as `Details`.
- Move selected compatible blocks into Details content.
- Leave the caret in the summary or first content paragraph.
- Create one undo entry.

`Wrap in Details` is permitted for:

- Paragraphs.
- Headings converted to normal content only after explicit confirmation.
- Lists.
- Code blocks.
- Images/figures.
- Quotes.

Initially reject:

- Tables if the official renderer cannot safely prefix/group them.
- Nested Details.
- Raw HTML/source-only blocks.
- A range crossing incompatible parents.

## 16.5 Markdown parser and serializer

Implement a Jotdex tokenizer/parser/renderer for the marker format in section 8.9.

Parsing rules:

1. Locate the exact opening marker on its own line.
2. Locate the next matching closing marker on its own line.
3. Reject nesting in 1.3.0.
4. Parse the first visible block as DetailsSummary.
5. Parse remaining blocks as DetailsContent.
6. If the summary/body cannot satisfy the schema, classify Source-only rather than dropping content.
7. Generic HTML-comment preservation must not consume these markers before the Details parser.

Serialization rules:

1. Write opening marker.
2. Write summary as one visible bold paragraph.
3. Write a blank line.
4. Serialize normal child blocks using the official Jotdex codec.
5. Write a blank line and closing marker.
6. Ensure surrounding block boundaries cannot fuse with headings, images, or lists.

## 16.6 Legacy HTML import

Recognize safe forms of:

```html
<details>
<summary>Title</summary>
...
</details>
```

Only convert to a visual Details node when:

- Tags are balanced.
- No script/event/unsafe style attributes exist.
- Summary is representable.
- Body content can be parsed without silent loss.

Otherwise open Source-only. Do not rewrite the legacy form merely by opening and closing the note.

## 16.7 Export and search

- Share HTML/static export should emit accessible native `<details>` and `<summary>`.
- Export may default to the same temporary open state chosen by export policy, but must not mutate the note.
- Search indexes both summary and body.
- Search snippets identify matches inside a Details section and show the summary.
- Outline may optionally show Details summaries as subordinate nonheading items, but they are not heading anchors.

## 16.8 Accessibility

- Use a native or equivalent disclosure control with `aria-expanded`.
- Summary is keyboard focusable.
- Enter/Space toggles disclosure without entering text-edit mode accidentally; editing summary remains possible by clicking/pressing an explicit edit affordance or moving the caret into text.
- Screen readers receive a meaningful label from the summary.

## 16.9 Suggested files

```text
src/Web/src/editor/details/
  JotdexDetails.ts
  DetailsView.tsx
  detailsMarkdown.ts
  detailsCommands.ts
  detailsValidation.ts
  details.test.ts
```

## 16.10 Acceptance criteria

- Details can contain paragraphs, lists, code, images, and supported blocks.
- Toggling open/closed causes no save request.
- Editing content causes normal autosave.
- Markdown remains readable outside Jotdex.
- Save/reopen restores summary and body.
- Unwrap restores child blocks in order in one undoable transaction.
- Malformed/nested markers force Source-only.
- Share/static export emits usable native Details content.

---

# 17. Feature 8 - text highlighting

## 17.1 Implementation

Add the official Highlight extension at the exact pinned Tiptap version, configured for one default color:

```ts
Highlight.configure({ multicolor: false })
```

If the stock official Markdown behavior is incomplete for `==text==`, extend it with Jotdex-owned parse/render methods and tests.

## 17.2 User experience

Expose Highlight in:

- Selection bubble menu.
- `More formatting` toolbar menu.
- Slash command when text is selected.
- Keyboard shortcut `Mod+Shift+H` unless it conflicts with an established Jotdex shortcut.

The user should see one consistent highlight style in light and dark themes.

## 17.3 Rules

- Do not support multiple persistent highlight colors in 1.3.0.
- Highlight must not apply in code blocks, inline code, math, raw comments, or link destinations.
- Pasted `<mark>` may become highlight after sanitation.
- `==` input rules must not fire inside code/math.
- Overlap with bold, italic, underline, strike, links, subscript, and superscript must round-trip or be blocked by a clear constraint.
- Clear formatting removes highlight.

## 17.4 Acceptance criteria

- `==text==` opens as highlighted text.
- New highlighted text saves as `==text==`.
- Repeated parse/serialize cycles preserve content and mark boundaries.
- Highlight next to punctuation, whitespace, links, and images remains valid.
- Share/static export styles `<mark>` accessibly.

---

# 18. Feature 9 - improved callouts

## 18.1 Product goal

Turn the existing typed callout into a polished block that can be retitled, retyped, optionally collapsed, moved, and edited without recreating it.

## 18.2 Callout attributes

Extend the callout schema with:

```ts
type CalloutAttrs = {
  type: 'note' | 'tip' | 'info' | 'warning' | 'danger'
  title: string | null
  collapsible: boolean
  defaultOpen: boolean
}
```

Do not store temporary current-open state in document attrs. Only `collapsible/defaultOpen` affect Markdown.

## 18.3 Visual design

A callout displays:

- Type-specific icon and semantic label.
- Editable title when present.
- Type selector.
- Optional disclosure toggle.
- Body supporting paragraphs, lists, code blocks, quotes, and images where round-trip-safe.
- Block handle and context menu.

Context actions:

- Change type.
- Add/Edit/Remove title.
- Make collapsible.
- Set default expanded/collapsed.
- Unwrap to normal blocks.
- Duplicate.
- Delete.

## 18.4 Command behavior

`Insert callout` creates:

- Selected type.
- Optional title.
- A body paragraph with caret.
- One undo step.

`Convert selection to callout` wraps compatible top-level blocks in one transaction. Reject incompatible ranges rather than flattening.

Changing type/title/collapse setting must preserve body and selection.

## 18.5 Markdown behavior

Implement the title/collapse syntax in section 8.10 through the current `JotdexCalloutMarkdown` extension.

Important cases:

- Existing `> [!warning]` remains unchanged when untouched.
- Title text must escape safely and remain one line.
- Empty title is equivalent to no title.
- Body lines remain correctly blockquoted, including blank lines, lists, and code fences.
- A heading after a callout must not fuse.
- A callout after an image must not fuse.
- Nested callouts are disallowed initially unless explicit tests prove safe.
- Details inside callouts and callouts inside Details require explicit compatibility tests before enabling.

## 18.6 Temporary collapse behavior

- Clicking disclosure does not dirty the note.
- `defaultOpen` changes only through an explicit menu setting.
- Navigating to a search hit or outline/deep link inside a collapsed callout must temporarily expand it.
- Export policy should preserve the intended default open/collapsed semantics in HTML.

## 18.7 Export/search/accessibility

- Share/static export retains callout type, title, and collapsibility.
- Search indexes title and body.
- Callout title appears in snippets.
- Warning/danger styling must not rely on color alone.
- Use appropriate accessible labels and disclosure semantics.

## 18.8 Acceptance criteria

- Existing callouts remain valid.
- Titles and collapse settings round-trip.
- Changing type does not alter body.
- Temporary open/close produces no PUT.
- Search/deep-link navigation expands hidden content.
- Share/static export renders typed, titled, collapsible callouts.
- Undo restores prior type/title/state in one step.

---

# 19. Feature 10 - live outline and section operations

## 19.1 Product goal

Replace the current regex-only outline with a live document-aware navigation and organization tool while retaining a Source-mode fallback.

## 19.2 Outline contents

For each heading, track:

- Level.
- Text.
- Document position.
- Runtime anchor/deep-link slug.
- Duplicate occurrence index.
- Active/above/below viewport state.
- Folded state.
- Section end position.
- Optional count of open tasks in the section.
- Optional section word count if inexpensive, but do not add a general note-statistics feature under this contract.

Render a nested tree reflecting heading levels. Do not require perfectly sequential heading levels; show structure as authored and optionally warn through formatting check.

## 19.3 Implementation approach

Build a Jotdex-owned ProseMirror plugin and React outline state adapter. Do not depend on a private-registry Table of Contents package.

Recommended files:

```text
src/Web/src/editor/outline/
  OutlinePlugin.ts
  OutlinePane.tsx
  outlineModel.ts
  headingSlug.ts
  sectionRange.ts
  sectionMove.ts
  outlineScrollSpy.ts
  outline.test.ts
```

Update the outline only on document changes, relevant fold-state changes, or throttled scroll events. Selection-only transactions must not rebuild/serialize the document.

## 19.4 Active heading and scroll spy

- Highlight the heading governing the current caret.
- While reading, highlight the heading nearest the top of the editor viewport.
- Keep the active outline row visible without aggressively stealing scroll.
- Account for the sticky/collapsing editor toolbar.
- Work in main pane and pop-out windows.
- Opening a note with a deep link should select/scroll only after the editor has parsed and rendered.

## 19.5 Click navigation and folds

Clicking a heading:

1. Temporarily expand any folded heading ancestor or collapsed callout/Details container needed to reveal it.
2. Scroll the heading into a comfortable viewport position.
3. Place the caret or node selection predictably without selecting all heading text.
4. Update active outline state.

The Outline should show fold controls that use the existing HeadingFold plugin rather than creating a second fold model.

## 19.6 Deep links

Use application query parameters rather than `window.location.hash`, because Jotdex already uses the hash for clipping payloads.

Recommended form:

```text
/?note=<note-id>&heading=<url-encoded-runtime-slug>
```

Slug rules:

- Lowercase.
- Normalize Unicode consistently.
- Remove or replace punctuation deterministically.
- Collapse whitespace to `-`.
- Duplicate headings receive occurrence suffixes such as `-2`, `-3`.
- Derive at runtime; do not write invisible heading IDs into Markdown.

`Copy link to heading` copies the full Jotdex URL. Moving/renaming the note may change the URL only according to existing stable note ID behavior. Editing heading text changes the heading portion, which is acceptable; the app should fall back to the closest matching heading where reasonable.

## 19.7 Section reordering

Dragging a heading in the Outline moves the complete section:

- Section begins at the heading node.
- Section ends immediately before the next heading of the same or higher level, or document end.
- Child subsections move with it.
- Do not permit dropping a section into its own range.
- A drop above/below another outline item must calculate valid mapped positions.
- Preserve marks, block nodes, metadata, images, callouts, Details, tasks, and tables.
- Dispatch one transaction and one undo event.
- Map the current selection when possible.
- Validate and serialize in tests.

Provide keyboard alternatives:

- Move section up.
- Move section down.
- Nest one level deeper when valid.
- Promote one level when valid.

Nesting/promoting changes heading levels for the entire section hierarchy only after explicit confirmation and must clamp to H1-H6.

## 19.8 Source-mode fallback

In Source mode:

- Continue to extract headings using a Markdown-aware parser rather than the current simplistic regex where possible.
- Outline navigation scrolls the textarea to the heading line.
- Section drag/reorder is disabled unless a safe line-range algorithm and tests exist.
- Show why advanced outline actions are unavailable.

## 19.9 Search and external navigation

When global search opens a note at a matching heading:

- Pass the intended heading/position into the same outline navigation path.
- Expand folded/collapsed containers as needed.
- Do not create a save merely by revealing the match.

## 19.10 Acceptance criteria

- Nested outline reflects the live document.
- Active heading updates while editing and scrolling.
- Click navigation handles folded sections.
- Deep links survive login and note loading.
- Duplicate headings resolve deterministically.
- Section drag moves all child content and round-trips.
- Keyboard section movement has parity.
- Outline updates do not serialize on every selection/scroll event.
- Source-mode outline still navigates safely.

---

# 20. Feature 11 - visible block insertion button

## 20.1 Foundation

Jotdex 1.2.2 already contains block-gap navigation that creates/selects paragraphs between top-level blocks and after the final block. Do not replace that behavior with a competing implementation.

Create a visible `+` affordance that uses the same gap calculations and insertion transaction.

## 20.2 User experience

When the pointer approaches a valid top-level gap, show a small plus button in the left gutter:

```text
Paragraph above

      +

Code block below
```

Clicking the plus:

1. Creates/selects an empty paragraph at that gap using the existing block-gap logic.
2. Opens the shared command menu anchored to that paragraph.
3. Allows immediate insertion of any compatible command.

After the final block, show the same affordance in the bottom editor padding.

## 20.3 Cancellation behavior

If the plus button created a temporary empty paragraph and the user cancels without typing or inserting content:

- Remove only that untouched paragraph if doing so does not remove a preexisting user-authored blank block.
- Do not create a save/history record for a no-op insertion/cancel sequence.

If the user types, moves the caret, pastes, or inserts a command, keep the paragraph/content normally.

Track temporary paragraphs by transaction metadata/operation ID, not by guessing that any empty paragraph should be deleted.

## 20.4 Valid gaps

Show the plus only:

- Between top-level blocks with a visible gap.
- Above the first block when layout permits.
- Below the final block.
- Outside code blocks, tables, lists, callouts, and Details internals unless a separately valid nested insertion point is implemented.
- While Visual mode is editable and not locked.

Hide it while:

- Dragging a block.
- Resizing an image.
- A modal is open.
- The editor is read-only or Source mode.
- The pointer is over interactive node controls.

## 20.5 Mobile behavior

On narrow/touch devices:

- Do not require hover.
- A tap in the left margin/gap reveals the plus button.
- Provide at least a 44 by 44 touch target without creating an oversized visible icon.
- The command menu opens as a bottom sheet or viewport-contained popover.
- Scrolling must not accidentally insert blocks.

## 20.6 Trailing-node policy

Do not automatically append a persistent empty paragraph on every note load merely to show the plus button. Opening a note without editing must remain a no-write operation.

Use the existing interaction-driven trailing insertion unless testing proves a view-only widget can provide the affordance without mutating the document.

## 20.7 Suggested files

```text
src/Web/src/editor/gaps/
  BlockInsertAffordance.tsx
  gapTarget.ts
  temporaryParagraph.ts
  blockInsert.test.ts
```

Refactor/share logic from `blockGapNavigation.ts` rather than copying position calculations.

## 20.8 Acceptance criteria

- Plus appears between stacked code blocks, images, tables, callouts, Details, and normal blocks.
- Clicking it opens the same command registry as `/`.
- Canceling a new empty insertion creates no lasting document change.
- Existing blank paragraphs are never deleted accidentally.
- Opening a note without using the affordance produces no PUT.
- Mobile insertion works without hover.
- Plus, slash menu, and drag handle do not overlap or fight for the same gutter.

---

# 21. Additional formatting - underline

## 21.1 Implementation

Add official `@tiptap/extension-underline` at the exact pinned version. Extend its official Markdown behavior if needed so the canonical representation is `<u>...</u>`.

Expose through:

- Selection bubble menu.
- `More formatting` menu.
- `Mod+U`, except where the browser intercepts it and Jotdex cannot safely override.
- Slash command for an active text selection.

## 21.2 Rules

- Underline is an inline mark only.
- Do not apply inside code or math.
- Avoid confusing link and underline styling: links must remain visually distinguishable by color/focus/hover, not underline alone.
- Underline may overlap supported marks only when the official serializer round-trips the combination.
- HTML paste with `text-decoration: underline` or `<u>` may become underline after sanitation.
- Clear formatting removes it.

## 21.3 Acceptance criteria

- `<u>text</u>` parses and serializes.
- Underline survives combinations with bold, italic, highlight, links, subscript, and superscript where permitted.
- Unsupported overlap is blocked or Source-only, never silently dropped.
- Export and static HTML show underline.

---

# 22. Additional formatting - subscript

## 22.1 Implementation

Add official `@tiptap/extension-subscript` with canonical `<sub>...</sub>` Markdown/HTML representation.

Expose through:

- Selection bubble menu More section.
- Top toolbar More formatting menu.
- `Mod+,` when it does not conflict with browser behavior.
- Slash command for a text selection.

## 22.2 Rules

- Subscript and superscript are mutually exclusive on the same character range.
- Applying subscript must remove superscript from that range in the same transaction.
- Do not apply in code or math nodes.
- Preserve ordinary Unicode subscripts as text; do not auto-convert them into marks.
- Clear formatting removes the mark but preserves text.

## 22.3 Acceptance criteria

- `H<sub>2</sub>O` round-trips.
- Applying subscript over superscript switches cleanly in one undo step.
- Mixed adjacent sub/sup content remains valid.
- Export renders semantic `<sub>`.

---

# 23. Additional formatting - superscript

## 23.1 Implementation

Add official `@tiptap/extension-superscript` with canonical `<sup>...</sup>` representation.

Expose through:

- Selection bubble menu More section.
- Top toolbar More formatting menu.
- `Mod+.` when it does not conflict.
- Slash command for a text selection.

## 23.2 Rules

- Applying superscript removes subscript from the same range.
- Do not transform typed `^2` automatically; safe typography has its own explicit policy.
- Do not apply in code or math.
- Preserve Unicode superscript characters as text.

## 23.3 Acceptance criteria

- `x<sup>2</sup>` round-trips.
- Sub/sup mutual exclusion is deterministic.
- Export renders semantic `<sup>`.

---

# 24. Additional formatting - text alignment

## 24.1 Product scope

Support alignment for top-level paragraphs and headings only:

- Left/default.
- Center.
- Right.
- Justify.

Do not initially align list items, table-cell paragraphs, blockquotes, callout body blocks, Details body blocks, code, or math.

## 24.2 Tiptap behavior

Use or extend `@tiptap/extension-text-align` with types `['paragraph', 'heading']`.

Do not rely on HTML style serialization from Tiptap. Add Jotdex-owned official Markdown parsing/serialization that uses the metadata comment in section 8.5.

The alignment metadata and next block must behave as one semantic unit for:

- Dragging.
- Duplicating.
- Cutting/copying.
- Section reordering.
- Undo/redo.
- Save-safety and semantic comparison.

## 24.3 UI

Expose in:

- Block actions menu.
- Toolbar More formatting menu.
- Selection bubble menu only when the selection stays in one eligible block.
- Keyboard shortcuts matching common conventions when safe:
  - `Mod+Shift+L` left.
  - `Mod+Shift+E` center.
  - `Mod+Shift+R` right.
  - `Mod+Shift+J` justify.

A shortcut conflict audit is mandatory before enabling.

## 24.4 Firefox and layout

Firefox has known interactions between justify and `white-space: pre-wrap`. Test the actual Jotdex CSS. If justify is visually broken in Firefox, either adjust paragraph CSS safely or disable Justify in Firefox with a visible reason. Do not claim support when it does not render correctly.

## 24.5 Source-only and malformed markers

- Unknown alignment values remain visible comments in Source mode and do not apply.
- Multiple alignment markers before one block force a formatting diagnostic; canonicalize only after explicit edit.
- A marker at end of file with no following block forces Source-only or remains raw; never attach it to a later block unexpectedly.
- An alignment marker before an ineligible node remains raw and must not disappear.

## 24.6 Acceptance criteria

- Eligible paragraphs/headings align and round-trip.
- Left removes the marker.
- Block moves preserve alignment.
- Other Markdown viewers ignore the comment and show content.
- Alignment does not leak to following blocks.
- Firefox behavior is tested/documented.

---

# 25. Additional formatting - mathematics

## 25.1 Product goal

Allow technical notes to contain readable, editable inline and block formulas rendered locally with KaTeX while preserving plain-text LaTeX in the Markdown file.

## 25.2 Extensions

Use `@tiptap/extension-mathematics` and `katex` at compatible pinned versions, but wrap/extend InlineMath and BlockMath so Jotdex owns the Markdown tokenizers and renderers.

Do not register a broad stock `$...$` tokenizer that could reinterpret ordinary currency, shell prompts, PowerShell variables, or existing technical text.

## 25.3 Canonical syntax

Inline:

```markdown
The area is \(\pi r^2\).
```

Block:

```markdown
\[
E = mc^2
\]
```

The parser must recognize only balanced delimiters outside code, raw HTML, and escaped regions.

## 25.4 User experience

### Inline math

- `Inline math` command opens a small LaTeX dialog.
- If text is selected, prefill the dialog with the selected text but do not convert until confirmed.
- Insert one inline atom.
- Clicking the atom opens Edit formula.
- Provide Edit, Copy LaTeX, Convert to text, and Delete.

### Block math

- `Math block` command opens a larger multiline LaTeX editor with preview.
- Insert one block node followed by a usable paragraph/gap.
- Provide Edit, Copy LaTeX, Convert to code/text, Duplicate, and Delete.

### Preview

- Render locally with KaTeX.
- Invalid LaTeX shows source plus an error indicator; it must not disappear.
- Preview errors do not block saving the LaTeX source unless the delimiters/document structure are invalid.

## 25.5 KaTeX safety configuration

Use conservative options:

- `throwOnError: false` in rendered view.
- `trust: false` unless a reviewed need exists.
- No user-provided global macros that can create unsafe URLs/HTML.
- Set maximum formula lengths to protect the browser, for example 10,000 characters inline and 50,000 block, subject to profiling.
- Catch render errors.
- Package CSS and required assets locally.
- Update third-party notices.

## 25.6 Editing and paste rules

- Do not automatically migrate `$...$` text.
- Do not interpret math delimiters in code blocks, inline code, raw comments, links, or file paths.
- Pasting LaTeX remains text unless the user is inside the math dialog/node or chooses Paste as math.
- Plain copy of a math node should copy the canonical LaTeX source or a useful text representation.
- Copy Markdown copies delimiters.

## 25.7 Search/export

- Search indexes LaTeX source.
- Search snippets may show the rendered formula label plus source.
- Share/static export renders KaTeX locally and includes source fallback.
- Static export must not need Internet access.
- Source mode edits delimiters directly.

## 25.8 Suggested files

```text
src/Web/src/editor/math/
  JotdexInlineMath.ts
  JotdexBlockMath.ts
  MathDialog.tsx
  MathNodeView.tsx
  mathMarkdown.ts
  mathValidation.ts
  math.test.ts
```

## 25.9 Acceptance criteria

- Currency such as `$5 and $10` remains ordinary text.
- PowerShell and code containing dollar signs remain unchanged.
- Inline/block formulas round-trip through the official codec.
- Invalid LaTeX remains recoverable/editable.
- Math works offline.
- Export and search include formula meaning/source.
- No unsafe KaTeX trust behavior is enabled.

---

# 26. Additional formatting - emoji

## 26.1 Product goal

Provide a searchable emoji picker and optional `:` suggestions while keeping the Markdown file ordinary Unicode text.

## 26.2 Extension behavior

Use `@tiptap/extension-emoji` only if it can serialize/copy as Unicode through the official Jotdex codec. Configure a locally packaged/default Unicode emoji list.

Do not use remote custom emoji images. If a fallback image is needed for an unsupported glyph, package it locally or fall back to the Unicode character/name without a network request.

## 26.3 User experience

- Slash command `Emoji` opens a searchable picker.
- Optional `:` trigger opens suggestions only after deliberate boundaries, not in URLs, times, IPv6 addresses, code, or metadata.
- Categories and recent emoji are allowed as local UI preferences.
- Keyboard navigation, Enter insertion, Escape closing.
- Insert at the mapped selection.

## 26.4 Canonical storage

Always serialize the Unicode character. Shortcodes such as `:warning:` are input/search aliases, not canonical note content.

Do not automatically replace ASCII emoticons such as `:)`, `:D`, or `;)` in technical notes.

## 26.5 Search/export/accessibility

- Search indexes the Unicode character and, where practical, an emoji name alias.
- Copy/paste outside Jotdex produces Unicode.
- Export displays Unicode and includes a text fallback.
- Picker buttons include accessible emoji names.

## 26.6 Acceptance criteria

- Picker inserts at the intended caret.
- Note file contains Unicode, not a node ID or remote URL.
- `http://host:5180`, `10:30`, IPv6, and code are not treated as emoji triggers.
- Works offline and across browsers.

---

# 27. Additional formatting - safe typography

## 27.1 Product goal

Offer optional prose conveniences without damaging commands, paths, quotes, flags, operators, scripts, or copied technical data.

## 27.2 Default setting

Add a user preference named `Smart typography` and default it to **off** for existing and new installations unless Joshua explicitly changes that product default after testing.

Turning it on affects future typing only. It must not rewrite existing note content or scan/convert the vault.

## 27.3 Allowed rules when enabled

Initially enable only lower-risk transformations in normal prose contexts:

- `...` to ellipsis.
- `(c)` to copyright.
- `(r)` to registered trademark.
- `(tm)` to trademark.
- `(sm)` to service mark.
- `+/-` to plus/minus.
- `1/2`, `1/4`, `3/4` to fraction characters only if surrounded by prose-safe boundaries.

Potentially allow left/right arrows only after tests prove they do not alter command examples.

## 27.4 Disabled rules

Disable these by default even when Smart typography is on:

- Smart opening/closing single quotes.
- Smart opening/closing double quotes.
- `--` to em dash.
- `!=` to not-equal.
- `2x3` or `2*3` to multiplication.
- `^2` and `^3` superscript conversion.
- `<<` and `>>` quotation conversion.

These patterns are common in scripts, command switches, comparisons, redirection, and technical notation.

## 27.5 Context restrictions

No typography rule may run in:

- Code blocks.
- Inline code.
- Math.
- URLs or link destinations.
- Raw HTML/comments.
- Task metadata.
- Wiki-link query text.
- File/path-like tokens.
- A paste operation.
- Source mode.

Only user typing in ordinary paragraphs, headings, callout prose, and Details prose is eligible.

## 27.6 Implementation

Use `@tiptap/extension-typography` with explicit rule configuration or implement a constrained Jotdex extension if the stock extension cannot enforce the required contexts.

Do not enable a whole default preset and then attempt to repair technical text after the fact.

## 27.7 UI

Place preference under an appropriate Settings/Editor section and optionally in a toolbar More menu:

```text
Smart typography: Off / On
```

Include concise help text explaining that it affects future prose typing only.

## 27.8 Acceptance criteria

- Default is off.
- Existing notes remain byte-identical on open/close.
- Code, URLs, commands, and paths never transform.
- Turning on the feature transforms only the approved patterns.
- Undo restores the original typed characters in one step.
- Preference persists through the existing UI preference system and move-kit behavior where appropriate.

---

# 28. Formatting compatibility and precedence

The agent must define and test mark compatibility explicitly. Do not rely on incidental ProseMirror mark rank.

## 28.1 Expected combinations

The following should normally be allowed when serializer tests pass:

- Bold + italic.
- Bold/italic + underline.
- Bold/italic/underline/strike + highlight.
- Link + bold/italic/underline/strike/highlight.
- Color/font size + ordinary emphasis marks.
- Subscript or superscript + ordinary emphasis marks.

## 28.2 Mutually exclusive or constrained combinations

- Subscript and superscript are mutually exclusive.
- Inline code excludes typography, color, font size, underline, highlight, subscript, superscript, and link.
- Inline math is an atom and cannot carry prose marks.
- Emoji nodes may inherit link only if the serializer and accessibility tests prove safe; otherwise links should surround adjacent text rather than the emoji atom.
- A link may not contain block nodes.
- Raw comment/metadata atoms do not accept formatting.

## 28.3 Overlapping-mark test requirement

For every allowed pair and important triple:

1. Create the marks in different start/end orders.
2. Serialize.
3. Parse again.
4. Compare semantic mark ranges.
5. Repeat for 20 cycles.
6. Test leading/trailing whitespace and punctuation.
7. Test next to an inline emoji and inline math atom.

If official Markdown cannot represent an overlap safely, choose one of:

- Normalize to unambiguous HTML for the overlapping segment using a documented renderer.
- Prevent the incompatible overlap with a clear UI message.
- Force Source-only for imported content that already contains it.

Never silently remove a mark.

---

# 29. Toolbar and menu information architecture

## 29.1 Keep the existing toolbar

Do not remove current toolbar actions. Reorganize only when feature parity, screenshots, mobile behavior, and keyboard navigation have been verified.

Recommended top-level toolbar:

```text
H1 H2 H3 | Bold Italic Code | List 1. Todo | Table Link Callout | Paste | More
```

`More` may include:

```text
Underline
Strike
Highlight
Text color
Font size
Subscript
Superscript
Alignment
Inline math
Emoji
Clear formatting
Smart typography preference/status
```

On small screens, the toolbar may wrap or collapse into grouped menus, but no command may become unreachable.

## 29.2 Context menus take precedence

- Text selection -> text Bubble Menu.
- Caret inside link -> Link popover.
- Image/figure selection -> Image inspector.
- Caret inside table -> Table controls.
- Callout selection/header -> Callout controls.
- Details summary/header -> Details controls.
- Math atom/block -> Math controls.
- Block gutter -> Drag handle/block actions.
- Empty paragraph or plus gap -> insertion menu.

The overlay coordinator determines precedence and prevents multiple surfaces from covering each other.

## 29.3 Icons

Use a local, consistent icon set already licensed for the project or small application-owned SVG paths. Do not pull icon files from a CDN. Every icon-only control needs an accessible label and tooltip.

## 29.4 Error communication

Commands that cannot execute should explain why, for example:

- `Merged cells are not supported by Markdown.`
- `Partial heading conversion is not available inside a list.`
- `This imported table contains multiple blocks in a cell. Edit it in Source mode.`
- `Math is not converted automatically from dollar amounts.`
- `This image is still uploading.`

Do not use silent no-ops.

---

# 30. Paste, copy, and clipboard integration

## 30.1 Preserve current code behavior

The existing exact-text code copy/paste path is non-negotiable. New menu features must not route code through HTML or Markdown insertion.

## 30.2 Rich paste

Extend the current smart paste path to recognize new safe HTML:

- `<mark>` -> highlight.
- `<u>` or underline style -> underline.
- `<sub>` -> subscript.
- `<sup>` -> superscript.
- Safe `<details>/<summary>` -> candidate Details conversion.
- Safe `<figure>/<figcaption>/<img>` -> candidate figure conversion.
- Text alignment styles -> strip by default or preserve only through explicit `Keep formatting` conversion to Jotdex alignment metadata.

The paste sanitizer must not accept arbitrary `style`, `data-*`, event handlers, scriptable URLs, or unknown figure/details attributes.

## 30.3 Plain paste

Plain paste remains literal prose text and does not interpret Markdown, emoji shortcode, math, typography, or link cards except the existing explicit selected-text URL convenience when the clipboard is exactly one safe URL.

## 30.4 Paste as code

Paste as code must remain exact except CRLF normalization. No typography, emoji, math, or rich-format conversion.

## 30.5 Copy behavior

- Copy selected prose should include useful `text/plain` and safe `text/html`.
- Copy from code stays plain characters.
- Copy a block through the block menu may offer `Copy as Markdown` and normal rich copy.
- Copy image may copy bytes when permitted.
- Copy math may offer source and rendered/plain variants.
- Copy Details/callout should include readable plain text and canonical Markdown where appropriate.

## 30.6 Cut behavior

Cut must preserve hidden metadata relationships. Cutting an aligned paragraph, bookmark card, figure, callout, or Details section must remove its complete canonical unit and permit paste/undo without orphan markers.

---

# 31. Autosave, revisions, conflicts, and history

## 31.1 No selection-only saves

Opening/closing menus, moving the caret, selecting text, scrolling the outline, expanding Details, expanding callouts, opening a lightbox, or previewing image resize must not dirty or serialize the document.

## 31.2 Commit boundaries

Mark transactions with meaningful operation kinds, including:

```text
slash-command
block-insert
block-move
block-transform
section-move
format-mark
alignment-change
table-structure
image-layout
image-replace
link-edit
link-card-convert
details-create
details-unwrap
callout-config
math-insert
math-edit
emoji-insert
```

Use the existing operation metadata and revision coordinator rather than inventing a parallel dirty flag.

## 31.3 Async operations

For image replacement, uploads, bookmark metadata, and other async actions:

- The local document revision may advance while the request is running.
- An old response may update attachment/cache metadata only if still relevant.
- It may not overwrite a newer document.
- It may not mark a newer revision Saved.
- It may not apply to a newly selected note.
- Conflicts continue to use the current ETag workflow.

## 31.4 History summaries

Improve history summaries for new operations where feasible:

- `Moved section "Network"`
- `Added 2 table columns`
- `Changed image width to 65%`
- `Changed callout to Warning`
- `Added Details section`
- `Edited formula`

Do not store sensitive note text in logs. History already contains note snapshots by design; operation summaries should be concise.

## 31.5 Open without edit

For every new syntax fixture, automated tests must prove:

- GET note.
- Open visual editor when safe.
- Wait beyond autosave debounce.
- Close/switch note.
- No PUT occurred.
- File hash and modified timestamp did not change.
- No history snapshot was created.

---

# 32. Search indexing and navigation

Update search extraction so new content remains findable.

## 32.1 Required indexed content

- Details summary and body.
- Callout title, type label, and body.
- Image alt text, title, caption, and filename.
- Link-card title, host, and URL text.
- Math LaTeX source.
- Emoji Unicode and optional name aliases.
- Underline/highlight/sub/sup text as normal text.
- Aligned text as normal text.
- Table headers/cells in reading order.

## 32.2 Search snippets

- A hit inside Details should identify the summary and expand the block when opened.
- A hit inside a collapsed callout should expand it temporarily.
- A heading hit should use the new outline/deep-link navigation path.
- A math-source hit should select or reveal the formula node.
- An image-caption/alt hit should select or scroll to the image.

## 32.3 Rebuild equivalence

Deleting and rebuilding the search index must produce equivalent searchable content. No new feature may depend on hidden index-only text that cannot be reconstructed from the Markdown/assets.

---

# 33. Share HTML, static export, and server rendering

## 33.1 Required parity

Update the server renderer and export styles for:

- Highlight.
- Underline.
- Subscript.
- Superscript.
- Text alignment.
- Details.
- Titled/collapsible callouts.
- Advanced image figures and captions.
- Bookmark cards or graceful normal-link fallback.
- Inline and block math.
- Emoji.
- Improved tables and column alignment.

## 33.2 Math export

Static export must include the required local KaTeX CSS/assets or pre-render formulas to safe HTML during export. It may not reference a CDN. Provide source fallback if rendering fails.

## 33.3 Details/callout export

- Details should become accessible `<details><summary>`.
- Collapsible callouts may become `<details>` styled as a callout or a callout containing an accessible disclosure.
- Noncollapsible callouts remain static alert blocks.
- Titled callouts display title separately from body.

## 33.4 Figure export

Convert `data-width` and `data-align` to safe export CSS classes or validated inline style. Do not carry arbitrary styles from the note.

## 33.5 Bookmark card export

Static export must work offline. If no cached metadata is included, render the title/host/link as a simple card. Do not make static pages fetch remote metadata when opened.

## 33.6 Sanitization

Update allowlists deliberately. New allowlist support must be exact rather than broad:

- Tags: `mark`, `u`, `sub`, `sup`, `details`, `summary`, `figure`, `figcaption`, and KaTeX-generated tags/classes only through a reviewed rendering path.
- Attributes: only required `data-jotdex-*`, `data-width`, `data-align`, safe `src`, `alt`, `title`, and accessibility attributes.
- Never allow `on*` handlers, arbitrary style, script, iframe, object, embed, form, or unsafe URLs.

## 33.7 Export tests

For each fixture:

- Render live server HTML.
- Produce Share HTML.
- Produce static export.
- Parse each output with an HTML parser.
- Confirm expected semantic elements and no forbidden content.
- Confirm every local asset exists in export.
- Confirm output opens without network access.

---

# 34. Security requirements

## 34.1 Authentication

All existing note and attachment endpoints remain authenticated. New endpoints for bookmark metadata, image metadata, or optional asset actions must follow the same authorization/cookie policy.

## 34.2 URL safety

Use centralized URL validation for links, images, bookmark metadata, and clip actions. Normalize before checking. Block encoded/obfuscated dangerous schemes.

## 34.3 SSRF

Any server-side remote metadata/image fetch must:

- Permit only HTTP/HTTPS.
- Resolve DNS and block loopback, private, link-local, multicast, and metadata-service addresses.
- Revalidate each redirect destination.
- Limit redirects.
- Limit response size and time.
- Validate content type.
- Avoid forwarding Jotdex cookies/credentials.
- Avoid returning arbitrary active HTML to the app origin.

## 34.4 HTML and attribute safety

Jotdex-owned comments/HTML must have strict parsers. Do not deserialize arbitrary JSON/attributes into DOM properties. Escape text/attributes during serialization.

## 34.5 Math safety

- KaTeX trust disabled.
- No arbitrary command execution.
- Formula length/rate limits.
- Rendering errors contained.
- No user-supplied CSS/HTML macros.

## 34.6 Clipboard safety

Do not put secret data on the clipboard without an explicit user action. Clipboard failures must not crash the editor.

## 34.7 Logging

Logs may include command IDs, node types, revision numbers, timing, and error codes. Do not log note bodies, selected text, formulas, captions, URLs with sensitive query strings, passwords, TOTP codes, cookies, or attachment contents.

---

# 35. Accessibility requirements

The project is not complete if features require a mouse or are unusable with a screen reader.

## 35.1 Menus

- Correct menu/listbox/toolbar semantics.
- Roving tabindex or equivalent predictable keyboard navigation.
- Active item announcement.
- Escape behavior.
- Focus return.
- Visible focus indicators.
- Disabled reason accessible to assistive technology.

## 35.2 Dragging

- Keyboard Move up/down/start/end.
- ARIA live movement announcements.
- Do not rely on color alone for drop position.

## 35.3 Images

- Alt/decorative controls.
- Keyboard resizing.
- Accessible inspector fields.
- Lightbox focus trap and Escape.

## 35.4 Tables

- Header cells are semantic.
- Table controls announce row/column impact.
- Keyboard cell navigation.
- Horizontal scrolling remains usable at zoom.

## 35.5 Details and callouts

- Accessible disclosure states.
- Type and title announced.
- Warning/danger not conveyed only by color.

## 35.6 Math and emoji

- Math nodes expose source/accessible text.
- Emoji picker exposes names.
- Emoji output remains readable Unicode.

## 35.7 Zoom and responsive behavior

Test at 200% browser zoom and narrow viewport. Menus must remain within the viewport and content controls must not obscure the editor irrecoverably.

---

# 36. Mobile and touch requirements

Required viewport classes:

- Narrow phone portrait.
- Phone landscape.
- Tablet portrait.
- Desktop.
- Pop-out note window.

Mobile-specific requirements:

- Slash/plus menus may render as bottom sheets.
- Bubble menu must not cover the selection or software keyboard unnecessarily.
- Drag handle must have touch alternatives; long-press may open block actions, but must not disable normal text selection.
- Image resize uses large handles and explicit width presets.
- Table controls may use a compact sheet and table horizontal scrolling.
- Outline may use a full-screen or slide-over pane.
- Link/math/image dialogs fit without background scroll bugs.
- No hover-only feature.

Use real WebKit mobile emulation tests where possible, but do not claim physical-device certainty without manual verification.

---

# 37. Performance requirements

## 37.1 Selection and scroll

- Selection changes must not serialize Markdown.
- Scroll spy must be throttled or use IntersectionObserver/efficient geometry.
- Drag hover must not cause React re-render of the entire editor on every pointer event.
- Bubble/link/table/image menu positioning must not reparse the document.

## 37.2 Large-note targets

Test at minimum:

- 200,000 characters.
- 2,000 top-level blocks.
- 200 headings.
- 50 images.
- 20 tables.
- 100 code blocks.
- Mixed callouts/Details/math/links.

Targets on a normal development PC:

- Slash menu opens within 100 ms after trigger.
- Bubble menu appears within 150 ms after selection settles.
- Outline update after a document edit within 200 ms without blocking typing.
- Block-handle hover feels immediate and does not serialize.
- Typing latency remains visually smooth.
- Autosave serialization remains debounced through the current coordinator.

Record benchmark conditions and results; do not use these as permission to weaken safety validation.

## 37.3 Memory cleanup

On note switch/unmount:

- Destroy menu plugins/listeners.
- Abort stale metadata requests/uploads where supported.
- Release selection bookmarks.
- Close dialogs.
- Destroy temporary KaTeX/NodeView resources.
- Prevent stale callbacks from applying to the next note.

---

# 38. Implementation sequence and milestone gates

Do not implement this as one uncontrolled change. Use the following work packages in order. Every completed work package must leave the repository buildable and tests green.

## EUX-00 - contract, baseline, recovery, and inventory

Deliverables:

- Copy this contract into `docs/decisions/editor-ux-expansion-contract.md`.
- Create ADR `0010-editor-ux-and-jotdex-dialect-v2.md`.
- Record baseline commit, package versions, and portable release.
- Retain a known-good 1.2.2 portable ZIP/executable.
- Create an isolated test vault and data directory.
- Hash a read-only copy of the real vault for audit; do not modify live files.
- Inventory every current toolbar button, shortcut, editor command, NodeView, custom node/mark, paste path, export renderer, sanitizer, and search extractor.
- Add stable checklist IDs to `CHECKLIST.md` and mark EUX active in `STATUS.md`.

Gate:

- Baseline build/test/publish succeeds before adding features.
- Protected E2E instance can be started and authenticated.
- Open-without-edit produces no PUT and no file change.

## EUX-01 - shared commands, overlays, and selection bookmarks

Deliverables:

- Shared command registry.
- Overlay coordinator.
- Selection bookmark abstraction.
- Common menu components and positioning.
- Command availability/context tests.
- No visible feature needs to ship yet.

Gate:

- Existing toolbar actions can call registry commands without behavior change.
- No raw ambiguous insertion paths are added.
- Selection survives dropdown/dialog open and apply.

## EUX-02 - slash menu and visible plus insertion

Includes features 1 and 11.

Deliverables:

- Slash trigger/menu.
- Plus button using existing block-gap logic.
- Mobile bottom-sheet behavior.
- Temporary paragraph cancellation.
- Core insertion commands wired to registry.

Gate:

- Full CMD/GAP unit and E2E matrix passes.
- Existing 1.2.2 block-gap tests remain green.
- No idle opening save.

## EUX-03 - bubble menu and inline formatting

Includes feature 3 plus highlight, underline, subscript, superscript.

Deliverables:

- Selection bubble menu.
- Mark extensions and official Markdown handlers.
- Mark compatibility rules/tests.
- More formatting menu.
- Clear formatting behavior.
- Export/search/sanitizer parity.

Gate:

- Schema coverage passes.
- 20-cycle mark combination round trips pass.
- Cross-browser selection/focus tests pass.

## EUX-04 - drag handle and block actions

Includes feature 2.

Deliverables:

- Top-level drag handle.
- Block actions menu.
- Keyboard block movement.
- Metadata-aware move/duplicate/cut/delete.
- Interaction with plus/bubble/outline foundations.

Gate:

- Every supported block can move/undo/redo.
- Hidden metadata remains attached.
- No pointer-move serialization.

## EUX-05 - Markdown-safe table editing

Includes feature 4.

Deliverables:

- Table insertion UI.
- Context controls.
- Row/column move operations.
- Column alignment.
- Spreadsheet/plain-grid paste.
- Cell key rules and validation.
- Export/mobile improvements.

Gate:

- No UI or key path can create a multi-block/merged cell.
- Table round-trip and browser matrix passes.

## EUX-06 - image inspector and figure dialect

Includes feature 5.

Deliverables:

- Standard/figure node design.
- Inspector, resize, replace, alt/caption/alignment.
- Lightbox/copy/download.
- Strict figure parser/serializer/sanitizer.
- Search/export parity.

Gate:

- Existing paste sessions remain green.
- Runtime URLs never reach Markdown.
- Replace failures preserve original image.
- Figure audit on vault copy produces no false conversion.

## EUX-07 - links and bookmark cards

Includes feature 6.

Deliverables:

- Link popover/dialog.
- Selected-text URL paste.
- Internal-note picker.
- Empty-line URL choice.
- Bookmark-card dialect/cache/view.
- SSRF-safe metadata retrieval if implemented server-side.

Gate:

- Unsafe schemes blocked.
- Bookmark card works offline.
- Internal links remain portable.

## EUX-08 - Details and improved callouts

Includes features 7 and 9.

Deliverables:

- Details nodes and marker dialect.
- Callout titles/collapse modes.
- Temporary disclosure state.
- Search/export/fold navigation.

Gate:

- Disclosure-only actions generate no save.
- Malformed markers force Source-only.
- Legacy callouts remain compatible.

## EUX-09 - outline and section movement

Includes feature 10.

Deliverables:

- Live outline model/plugin.
- Active heading/scroll spy.
- Fold/collapse navigation integration.
- Deep-link query handling.
- Section move/nest/promote and keyboard parity.
- Source-mode fallback.

Gate:

- Complete section movement round-trips.
- No selection/scroll serialization.
- Deep links work after protected login.

## EUX-10 - alignment, mathematics, emoji, and typography

Includes remaining formatting features.

Deliverables:

- Alignment metadata binding.
- Jotdex math nodes/tokenizers/dialogs/KaTeX.
- Emoji picker/Unicode serialization.
- Smart typography setting and constrained rules.
- Search/export/security support.

Gate:

- Currency/PowerShell/code collision tests pass.
- Math/emoji work offline.
- Typography defaults off and never alters existing content.

## EUX-11 - integrated hardening and vault-copy audit

Deliverables:

- Combined stress fixture.
- Full schema coverage.
- Full migration audit of vault copy.
- Source-only classifications.
- Performance benchmarks.
- Accessibility checks.
- Browser and mobile E2E.
- No live apply.

Gate:

- All automated tests pass.
- Audit report reviewed.
- No unexplained semantic changes.

## EUX-12 - documentation, packaging, and release

Deliverables:

- `docs/vault-format.md` updated.
- Changelog entry explaining why and how.
- README/help/shortcuts updated.
- `jotdexAiPrompt.ts` updated.
- Third-party notices updated.
- Portable publish produced and smoke tested on a clean Windows environment without Node.js.
- Previous portable retained for rollback.
- Version/tag/release notes prepared.

Final gate:

- Release only as 1.3.0 after every Definition of Done item is satisfied.

---

# 39. Feature flags and rollout

## 39.1 Development flags

During implementation, allow narrowly scoped development flags such as:

```text
editorSlashMenu
editorBlockHandles
editorBubbleMenu
editorTableControls
editorFigureImages
editorLinkCards
editorDetails
editorCalloutV2
editorOutlineV2
editorMath
editorEmoji
editorSmartTypography
```

Requirements:

- Flags are development/rollout controls, not permanent duplicate implementations.
- Existing behavior remains available until replacement parity is proven.
- Persistent content created under a flag must still parse safely when the UI flag is off; otherwise do not allow content creation under that flag.
- Remove obsolete flags or set stable defaults before release.
- Do not create a per-note legacy Markdown-engine flag.

## 39.2 Internal soak order

Recommended enable order:

1. Commands/plus/slash.
2. Bubble menu and safe inline marks.
3. Drag handle.
4. Table controls.
5. Image inspector.
6. Links/bookmark cards.
7. Details/callouts.
8. Outline.
9. Alignment/math/emoji/typography.

Each stage should be used against the isolated/staged vault before enabling the next high-risk feature.

## 39.3 Rollback

Program rollback:

- Stop Jotdex.
- Restore previous portable executable/program files.
- Keep vault/data separate.
- Confirm 1.2.2 can still read notes that do not use new 1.3.0 syntax.

Content rollback:

- New 1.3.0 syntax may not render visually in 1.2.2, so before broad personal use, retain backups/history and document this limitation.
- A downgrade scanner must identify notes containing new dialect syntax.
- Do not claim full program downgrade compatibility for notes that intentionally use new features.
- Markdown remains readable/source-editable even when the older visual editor cannot represent the syntax.

---

# 40. Required code organization

The agent may adapt names, but must preserve separation of concerns.

Recommended final structure:

```text
src/Web/src/editor/
  commands/
  menus/
  slash/
  blocks/
  gaps/
  tables/
  images/
  links/
  details/
  callouts/
  outline/
  formatting/
  math/
  emoji/
  typography/
  extensions/
  markdown/
  operations/
  revisions/
  paste/
  testing/
```

Rules:

- `NoteEditor.tsx` composes features and routes events; it must not become the implementation file for all features.
- Persistent syntax lives in extension/markdown modules.
- React menus/dialogs do not contain serializer regexes.
- Server export/search/sanitizer changes remain in their corresponding backend services.
- Shared concepts such as heading slugs and Jotdex marker parsing should have one implementation per runtime, with cross-language fixtures proving equivalence.
- Avoid a generic `utils.ts` dumping ground.

---

# 41. Automated test architecture

## 41.1 Unit tests

Use Vitest/jsdom for:

- Commands and availability.
- Markdown parse/render.
- ProseMirror transactions.
- Mark overlaps.
- Selection mapping.
- Section ranges/movement.
- Marker parsing.
- Validation and safety.
- Search extractors where frontend-owned.

## 41.2 Integration tests

Use frontend editor instances with the production extension set and production codec. Do not build tests from simplified extension sets except isolated upstream spikes.

Every persistent test should use:

```text
source Markdown
-> official Jotdex parse
-> editor transaction
-> official Jotdex serialize
-> parse again
-> semantic comparison
```

## 41.3 Backend tests

Use xUnit/integration tests for:

- Server rendering.
- Static/Share export.
- Search extraction.
- Sanitizer allowlists.
- Attachment and bookmark metadata endpoints.
- Auth/authorization.
- Migration/audit behavior.

## 41.4 Browser E2E

Use Playwright across:

- Chromium.
- Firefox.
- WebKit.

At least one narrow/mobile project should be added for editor controls. Physical iPhone/iPad validation should be documented as manual if not available.

## 41.5 Deterministic race testing

Use injectable delayed transports and fake timers for:

- Image upload/replace.
- Bookmark metadata.
- Autosave responses.
- Note switching.
- Menu selection bookmarks.
- Stale async callbacks.

Race-sensitive tests must run at least 50 iterations with deterministic delay orderings before release.

---

# 42. Detailed test matrix - commands and menus

## CMD-01 through CMD-20

- **CMD-01:** `/` at empty paragraph opens default commands.
- **CMD-02:** `/hea` ranks headings ahead of unrelated commands.
- **CMD-03:** slash does not trigger in code block.
- **CMD-04:** slash does not trigger in inline code.
- **CMD-05:** slash does not trigger in math.
- **CMD-06:** slash does not trigger during IME composition.
- **CMD-07:** Arrow navigation wraps or clamps according to documented behavior.
- **CMD-08:** Enter executes one command.
- **CMD-09:** Tab executes one command.
- **CMD-10:** Escape closes without deleting query.
- **CMD-11:** deleting trigger closes menu.
- **CMD-12:** clicking a command preserves target position.
- **CMD-13:** async Image command resolves in original note/position.
- **CMD-14:** note switch discards stale async command result.
- **CMD-15:** disabled command exposes reason.
- **CMD-16:** registry command IDs are unique.
- **CMD-17:** slash, plus, toolbar call the same command implementation.
- **CMD-18:** mobile menu remains within viewport.
- **CMD-19:** idle lock closes menu and blocks execution.
- **CMD-20:** command transaction undoes in one step.

## MENU-01 through MENU-16

- **MENU-01:** only one primary overlay opens at a time.
- **MENU-02:** text bubble gives precedence to link popover when caret is in a link.
- **MENU-03:** image inspector replaces generic bubble for node selection.
- **MENU-04:** table controls appear only inside a table.
- **MENU-05:** block menu locks/unlocks drag handle correctly.
- **MENU-06:** Escape closes topmost surface.
- **MENU-07:** editor focus returns after close.
- **MENU-08:** scrolling repositions or closes stale popovers safely.
- **MENU-09:** toolbar auto-hide does not hide an open modal.
- **MENU-10:** note pop-out menus position correctly.
- **MENU-11:** 200% zoom does not clip critical controls.
- **MENU-12:** menu items expose accessible names/states.
- **MENU-13:** pointer down does not collapse selection.
- **MENU-14:** source-mode switch closes all visual editor overlays.
- **MENU-15:** conflict document replacement closes stale overlays/bookmarks.
- **MENU-16:** no menu interaction triggers Markdown serialization by itself.

---

# 43. Detailed test matrix - block insertion and movement

## GAP-01 through GAP-18

- **GAP-01:** plus appears between two paragraphs.
- **GAP-02:** plus appears between stacked code blocks.
- **GAP-03:** plus appears between stacked images.
- **GAP-04:** plus appears between table and callout.
- **GAP-05:** plus appears after final atom block.
- **GAP-06:** plus does not appear inside code.
- **GAP-07:** click creates/selects exactly one paragraph.
- **GAP-08:** command replaces temporary paragraph cleanly.
- **GAP-09:** cancel removes only operation-owned untouched paragraph.
- **GAP-10:** user typing preserves paragraph.
- **GAP-11:** existing empty paragraph is never deleted.
- **GAP-12:** opening a note causes no automatic trailing mutation.
- **GAP-13:** mobile tap shows plus without accidental insert during scroll.
- **GAP-14:** plus hides during block drag.
- **GAP-15:** plus hides during image resize.
- **GAP-16:** existing 1.2.2 gap click still works without plus.
- **GAP-17:** undo restores pre-insertion document.
- **GAP-18:** source/visual switch leaves no temporary marker.

## DRAG-01 through DRAG-24

- **DRAG-01:** move paragraph above heading.
- **DRAG-02:** move heading below paragraph.
- **DRAG-03:** move code block.
- **DRAG-04:** move entire table.
- **DRAG-05:** move standard image.
- **DRAG-06:** move figure with caption/attrs.
- **DRAG-07:** move callout as one block.
- **DRAG-08:** move Details as one block.
- **DRAG-09:** move block math.
- **DRAG-10:** move list wrapper without splitting items.
- **DRAG-11:** aligned block carries alignment metadata.
- **DRAG-12:** link card carries marker.
- **DRAG-13:** pending upload placeholder moves and resolves by ID.
- **DRAG-14:** invalid drop changes nothing.
- **DRAG-15:** drop into code is rejected.
- **DRAG-16:** drag outside editor does not delete content.
- **DRAG-17:** one undo restores order.
- **DRAG-18:** redo re-applies order.
- **DRAG-19:** keyboard Move up parity.
- **DRAG-20:** keyboard Move down parity.
- **DRAG-21:** move announcement is accessible.
- **DRAG-22:** pointer movement does not serialize.
- **DRAG-23:** folded heading handle moves only heading, not hidden section.
- **DRAG-24:** full Markdown order after reopen matches editor order.

---

# 44. Detailed test matrix - inline formatting

## FMT-01 through FMT-30

- **FMT-01:** highlight parses from `==text==`.
- **FMT-02:** highlight serializes to `==text==`.
- **FMT-03:** underline parses/serializes `<u>`.
- **FMT-04:** subscript parses/serializes `<sub>`.
- **FMT-05:** superscript parses/serializes `<sup>`.
- **FMT-06:** subscript removes superscript on same range.
- **FMT-07:** superscript removes subscript on same range.
- **FMT-08:** clear formatting preserves text/whitespace.
- **FMT-09:** clear formatting preserves links.
- **FMT-10:** clear formatting preserves block structure.
- **FMT-11:** selection bubble shows active state.
- **FMT-12:** selection bubble shows mixed state.
- **FMT-13:** selection remains after More dropdown opens.
- **FMT-14:** marks do not apply in code block.
- **FMT-15:** marks do not apply in inline code when incompatible.
- **FMT-16:** marks do not apply to math atom.
- **FMT-17:** HTML paste `<mark>` maps safely.
- **FMT-18:** HTML paste underline maps safely.
- **FMT-19:** leading whitespace remains outside mark delimiters when needed.
- **FMT-20:** trailing whitespace remains outside mark delimiters when needed.
- **FMT-21:** punctuation next to highlight round-trips.
- **FMT-22:** bold + italic + underline round-trips.
- **FMT-23:** link + bold + highlight round-trips.
- **FMT-24:** color + font size + underline round-trips.
- **FMT-25:** subscript + bold round-trips.
- **FMT-26:** superscript + italic round-trips.
- **FMT-27:** mark adjacent to emoji round-trips.
- **FMT-28:** mark adjacent to inline math round-trips.
- **FMT-29:** 20 repeated parse/serialize cycles preserve semantic ranges.
- **FMT-30:** Share/static export preserves semantic tags/styles.

## ALIGN-01 through ALIGN-18

- **ALIGN-01:** center marker applies to next paragraph only.
- **ALIGN-02:** center marker applies to heading.
- **ALIGN-03:** right alignment round-trips.
- **ALIGN-04:** justify alignment round-trips or is disabled with tested Firefox reason.
- **ALIGN-05:** left removes marker.
- **ALIGN-06:** block move carries marker.
- **ALIGN-07:** duplicate carries marker once.
- **ALIGN-08:** delete removes marker and block together.
- **ALIGN-09:** section move preserves marker association.
- **ALIGN-10:** marker before ineligible node remains raw/Source-only.
- **ALIGN-11:** dangling marker is not lost.
- **ALIGN-12:** unknown value remains raw.
- **ALIGN-13:** duplicate markers produce diagnostic.
- **ALIGN-14:** alignment does not leak to following block.
- **ALIGN-15:** alignment command is disabled in list/table/callout/Details/code.
- **ALIGN-16:** source edit then visual parse is stable.
- **ALIGN-17:** export alignment matches editor.
- **ALIGN-18:** another Markdown parser ignores marker while retaining text.

---

# 45. Detailed test matrix - tables

## TABLE-01 through TABLE-32

- **TABLE-01:** insert default 3 by 3 table with header.
- **TABLE-02:** insert configured rows/columns.
- **TABLE-03:** insertion replaces eligible empty host paragraph.
- **TABLE-04:** insertion leaves usable caret after table.
- **TABLE-05:** add row above.
- **TABLE-06:** add row below.
- **TABLE-07:** delete row.
- **TABLE-08:** add column left.
- **TABLE-09:** add column right.
- **TABLE-10:** delete column.
- **TABLE-11:** move row up.
- **TABLE-12:** move row down.
- **TABLE-13:** move column left.
- **TABLE-14:** move column right.
- **TABLE-15:** toggle header row.
- **TABLE-16:** set left column alignment.
- **TABLE-17:** set center column alignment.
- **TABLE-18:** set right column alignment.
- **TABLE-19:** delete table.
- **TABLE-20:** Tab navigation forward.
- **TABLE-21:** Shift+Tab backward.
- **TABLE-22:** final-cell Tab behavior matches documented rule.
- **TABLE-23:** Enter cannot create second block in cell.
- **TABLE-24:** safe hard break round-trips as `<br>` or documented equivalent.
- **TABLE-25:** spreadsheet HTML paste preserves rectangular data.
- **TABLE-26:** tab/newline plain grid offers Paste as table.
- **TABLE-27:** unsupported nested content is rejected/explained.
- **TABLE-28:** no exposed merge/split operation.
- **TABLE-29:** validator catches programmatically injected multi-block cell.
- **TABLE-30:** validator catches control characters.
- **TABLE-31:** 20-cycle round trip preserves all cells/alignment.
- **TABLE-32:** mobile export/editor horizontal overflow remains contained.

Every TABLE structural test must also assert one-step undo and valid Markdown after reopen.

---

# 46. Detailed test matrix - images

## IMG2-01 through IMG2-34

Use `IMG2` to distinguish this project from the existing image reliability matrix.

- **IMG2-01:** simple image remains standard Markdown.
- **IMG2-02:** setting caption upgrades to figure.
- **IMG2-03:** setting width upgrades to figure.
- **IMG2-04:** setting center/right alignment upgrades to figure.
- **IMG2-05:** reset all advanced attrs downgrades to standard image.
- **IMG2-06:** width clamps at 20/100.
- **IMG2-07:** drag preview does not dirty.
- **IMG2-08:** pointer release commits one width transaction.
- **IMG2-09:** Escape cancels resize.
- **IMG2-10:** keyboard resize changes by documented increment.
- **IMG2-11:** aspect ratio preserved.
- **IMG2-12:** alt text escapes Markdown/HTML safely.
- **IMG2-13:** caption escapes HTML safely.
- **IMG2-14:** decorative empty alt is allowed.
- **IMG2-15:** informative empty alt shows warning.
- **IMG2-16:** replace succeeds and preserves layout metadata.
- **IMG2-17:** replace failure preserves original image.
- **IMG2-18:** stale replace result after note switch is discarded.
- **IMG2-19:** user edits note while replacement uploads; result maps safely.
- **IMG2-20:** runtime `/api/attachments` URL is never serialized.
- **IMG2-21:** `blob:`/`data:`/pending URL is save-blocked.
- **IMG2-22:** Copy canonical path returns vault-relative path.
- **IMG2-23:** Copy image succeeds/falls back gracefully.
- **IMG2-24:** Download requires authentication.
- **IMG2-25:** lightbox requires authentication and traps focus.
- **IMG2-26:** broken image status does not dirty note.
- **IMG2-27:** missing image offers Replace/Remove.
- **IMG2-28:** figure block drag preserves attrs/caption.
- **IMG2-29:** section move preserves figure.
- **IMG2-30:** 20-cycle figure round trip preserves semantic attrs.
- **IMG2-31:** Share HTML renders caption/width/alignment.
- **IMG2-32:** static export copies asset and works offline.
- **IMG2-33:** search indexes alt/title/caption/file.
- **IMG2-34:** existing transactional image-paste tests remain green.

---

# 47. Detailed test matrix - links

## LINK-01 through LINK-28

- **LINK-01:** paste single safe URL over selected text creates link.
- **LINK-02:** multiline clipboard does not trigger selected-text URL behavior.
- **LINK-03:** unsafe scheme rejected.
- **LINK-04:** obfuscated unsafe scheme rejected.
- **LINK-05:** relative internal note link accepted.
- **LINK-06:** internal note picker inserts correct relative path.
- **LINK-07:** link popover displays current text/URL.
- **LINK-08:** edit text preserves URL.
- **LINK-09:** edit URL preserves text.
- **LINK-10:** remove link preserves text.
- **LINK-11:** copy address.
- **LINK-12:** copy Markdown.
- **LINK-13:** open internal link navigates to note.
- **LINK-14:** URL pasted on empty line shows conversion options.
- **LINK-15:** convert normal link to bookmark card.
- **LINK-16:** convert bookmark card back to normal link.
- **LINK-17:** bookmark marker round-trips.
- **LINK-18:** metadata success enhances card without changing Markdown.
- **LINK-19:** metadata failure leaves usable card.
- **LINK-20:** offline card works.
- **LINK-21:** stale metadata response after note switch is discarded.
- **LINK-22:** SSRF private address blocked.
- **LINK-23:** redirect to private address blocked.
- **LINK-24:** metadata response size/time limited.
- **LINK-25:** block drag carries card marker.
- **LINK-26:** section move carries marker.
- **LINK-27:** note rename/move preserves existing internal link behavior.
- **LINK-28:** export renders safe accessible link/card.

---

# 48. Detailed test matrix - Details and callouts

## DET-01 through DET-24

- **DET-01:** insert empty Details with default summary/body.
- **DET-02:** wrap compatible selected blocks.
- **DET-03:** unwrap restores blocks/order.
- **DET-04:** summary edit round-trips.
- **DET-05:** paragraph body round-trips.
- **DET-06:** list body round-trips.
- **DET-07:** code body round-trips.
- **DET-08:** image/figure body round-trips.
- **DET-09:** toggle closed produces no dirty/save.
- **DET-10:** session reopen uses nonpersistent open state policy.
- **DET-11:** block drag moves whole Details.
- **DET-12:** section move preserves Details.
- **DET-13:** nested Details insertion blocked.
- **DET-14:** malformed opening marker Source-only.
- **DET-15:** missing closing marker Source-only.
- **DET-16:** duplicate closing marker remains recoverable.
- **DET-17:** standard safe HTML details import.
- **DET-18:** unsafe HTML details Source-only.
- **DET-19:** search indexes summary/body.
- **DET-20:** search hit expands Details temporarily.
- **DET-21:** copy/cut preserves marker unit.
- **DET-22:** Share HTML uses native details/summary.
- **DET-23:** static export works offline.
- **DET-24:** 20-cycle parse/serialize preserves semantics.

## CAL-01 through CAL-26

- **CAL-01:** existing titleless note callout round-trips unchanged.
- **CAL-02:** each existing callout type round-trips.
- **CAL-03:** title parses/serializes.
- **CAL-04:** collapsed-default `-` parses/serializes.
- **CAL-05:** expanded-default `+` parses/serializes.
- **CAL-06:** temporary toggle no save.
- **CAL-07:** changing default state saves.
- **CAL-08:** changing type preserves body.
- **CAL-09:** removing title preserves body.
- **CAL-10:** unwrap restores body blocks.
- **CAL-11:** convert selection wraps compatible blocks.
- **CAL-12:** incompatible selection rejected.
- **CAL-13:** list body valid.
- **CAL-14:** code fence body valid.
- **CAL-15:** blank lines remain correctly quoted.
- **CAL-16:** image before callout boundary valid.
- **CAL-17:** heading after callout boundary valid.
- **CAL-18:** nested callout blocked.
- **CAL-19:** Details/callout nesting follows documented compatibility rule.
- **CAL-20:** search indexes title/body/type.
- **CAL-21:** search hit expands collapsed callout.
- **CAL-22:** outline/deep link reveals hidden heading inside supported callout context.
- **CAL-23:** Share HTML styles type/title/disclosure.
- **CAL-24:** static export styles type/title/disclosure.
- **CAL-25:** warning/danger accessible without color.
- **CAL-26:** legacy HTML `data-callout` parsing remains compatible.

---

# 49. Detailed test matrix - outline

## OUT-01 through OUT-32

- **OUT-01:** headings produce nested outline.
- **OUT-02:** skipped heading levels remain represented.
- **OUT-03:** duplicate headings receive deterministic suffixes.
- **OUT-04:** Unicode headings slug deterministically.
- **OUT-05:** punctuation-only heading has safe fallback slug.
- **OUT-06:** caret updates active heading.
- **OUT-07:** scroll updates active heading.
- **OUT-08:** sticky toolbar offset handled.
- **OUT-09:** click scrolls heading into view.
- **OUT-10:** click expands folded ancestor.
- **OUT-11:** click reveals collapsed Details/callout content when needed.
- **OUT-12:** fold controls use existing HeadingFold state.
- **OUT-13:** copy heading link uses query params, not hash.
- **OUT-14:** deep link survives unauthenticated redirect/login.
- **OUT-15:** deep link opens correct note.
- **OUT-16:** missing slug falls back gracefully.
- **OUT-17:** move section up.
- **OUT-18:** move section down.
- **OUT-19:** child subsections move with parent.
- **OUT-20:** cannot drop section into itself.
- **OUT-21:** section move preserves alignment markers.
- **OUT-22:** section move preserves link cards.
- **OUT-23:** section move preserves figures/Details/callouts/tables/tasks.
- **OUT-24:** one undo restores exact section range.
- **OUT-25:** redo restores move.
- **OUT-26:** keyboard movement parity.
- **OUT-27:** promote heading hierarchy safely.
- **OUT-28:** demote/nest hierarchy safely.
- **OUT-29:** H1/H6 limits enforced.
- **OUT-30:** selection/scroll does not serialize.
- **OUT-31:** Source-mode outline navigates textarea lines.
- **OUT-32:** global search heading navigation uses outline path.

---

# 50. Detailed test matrix - math, emoji, typography

## MATH-01 through MATH-28

- **MATH-01:** inline `\(...\)` parses.
- **MATH-02:** inline math serializes canonical delimiters.
- **MATH-03:** block `\[...\]` parses.
- **MATH-04:** block math serializes canonical delimiters.
- **MATH-05:** inline dialog inserts at bookmarked position.
- **MATH-06:** block dialog inserts one node.
- **MATH-07:** edit formula preserves node position.
- **MATH-08:** copy LaTeX.
- **MATH-09:** convert to text.
- **MATH-10:** invalid LaTeX remains visible/editable.
- **MATH-11:** invalid LaTeX does not execute HTML/URLs.
- **MATH-12:** currency `$5 and $10` remains text.
- **MATH-13:** PowerShell `$env:Path` remains text.
- **MATH-14:** code dollar signs remain exact.
- **MATH-15:** escaped delimiters remain text.
- **MATH-16:** unbalanced delimiters produce diagnostic/no loss.
- **MATH-17:** note switch discards stale dialog result.
- **MATH-18:** inline length limit.
- **MATH-19:** block length limit.
- **MATH-20:** KaTeX trust disabled.
- **MATH-21:** works offline.
- **MATH-22:** search indexes LaTeX.
- **MATH-23:** Share HTML render/source fallback.
- **MATH-24:** static export offline render.
- **MATH-25:** block drag/section move preserves math.
- **MATH-26:** 20-cycle round trip.
- **MATH-27:** accessibility label/source available.
- **MATH-28:** no math interaction saves on selection/preview only.

## EMOJI-01 through EMOJI-18

- **EMOJI-01:** picker opens/searches.
- **EMOJI-02:** keyboard navigation/insertion.
- **EMOJI-03:** Unicode written to Markdown.
- **EMOJI-04:** copy returns Unicode.
- **EMOJI-05:** recent list local preference.
- **EMOJI-06:** colon trigger does not fire in URL.
- **EMOJI-07:** colon trigger does not fire in time.
- **EMOJI-08:** colon trigger does not fire in IPv6.
- **EMOJI-09:** colon trigger does not fire in code.
- **EMOJI-10:** emoticons not auto-replaced.
- **EMOJI-11:** no remote image request.
- **EMOJI-12:** unsupported rendering fallback remains text.
- **EMOJI-13:** emoji next to marks round-trips.
- **EMOJI-14:** emoji in heading/outline works.
- **EMOJI-15:** emoji search alias works after index rebuild.
- **EMOJI-16:** export works offline.
- **EMOJI-17:** accessibility names in picker.
- **EMOJI-18:** stale picker selection after note switch discarded.

## TYPO-01 through TYPO-20

- **TYPO-01:** default off.
- **TYPO-02:** existing notes unchanged when setting off.
- **TYPO-03:** ellipsis transforms when enabled.
- **TYPO-04:** copyright transforms when enabled.
- **TYPO-05:** registered/trademark/service marks transform.
- **TYPO-06:** plus/minus transforms.
- **TYPO-07:** approved fractions transform at safe boundaries.
- **TYPO-08:** smart quotes remain disabled.
- **TYPO-09:** double dash remains unchanged.
- **TYPO-10:** not-equal remains unchanged.
- **TYPO-11:** multiplication patterns unchanged.
- **TYPO-12:** caret superscripts unchanged.
- **TYPO-13:** redirects/angle patterns unchanged.
- **TYPO-14:** code unchanged.
- **TYPO-15:** inline code unchanged.
- **TYPO-16:** URL/path unchanged.
- **TYPO-17:** paste unchanged.
- **TYPO-18:** undo restores typed source sequence.
- **TYPO-19:** preference persists safely.
- **TYPO-20:** disabling after use affects future typing only.

---

# 51. Password-protected end-to-end test contract

## 51.1 Required isolated environment

Create or extend a script such as:

```text
scripts/run-editor-ux-e2e.ps1
```

The script must:

1. Create a temporary vault directory.
2. Populate it with editor UX fixtures and assets.
3. Create a temporary Jotdex application data directory.
4. Choose an available loopback port.
5. Start the actual built Jotdex server, not only Vite.
6. Wait for `/api/health`.
7. Use `/api/auth/setup` to create an admin account and a random test password.
8. Store the password only in process memory/environment or a protected temporary file excluded from Git.
9. Save Playwright authenticated storage state.
10. Run the browser projects.
11. Stop the server.
12. Preserve traces/reports only on failure.
13. Delete secrets and temporary auth state on success.

Do not use the real vault, real data directory, real password, or real cloud credentials.

## 51.2 Authentication setup tests

- Anonymous `/api/notes` access receives 401 when password is configured.
- Setup creates the account and normal auth cookie.
- Login through the UI works.
- Login through API setup project works.
- Logout invalidates access.
- Idle-lock overlay closes editor menus.
- After unlock, stale editor operations do not apply automatically.
- TOTP-enabled mode should have at least one separate test path if the test harness can safely create/verify a local TOTP secret.

## 51.3 Protected editor workflows

At minimum, the browser suite must execute these against the real server and real disk files:

### E2E-01 - slash insertion

- Log in.
- Open fixture note.
- Use `/warning` to insert callout.
- Save.
- Reload.
- Verify visual callout and raw Markdown file.

### E2E-02 - plus insertion

- Open note ending in image/code block.
- Use final plus button.
- Insert paragraph or code block.
- Verify no fused Markdown boundary.

### E2E-03 - formatting

- Select text.
- Apply underline, highlight, subscript/superscript.
- Save/reload.
- Verify file syntax and visual result.

### E2E-04 - block move

- Drag block.
- Save/reload.
- Verify disk order.
- Undo/redo before save in a second run.

### E2E-05 - table

- Insert table.
- Add row/column.
- Set column alignment.
- Save/reload.
- Verify raw GFM table.

### E2E-06 - image figure

- Upload image.
- Set caption, width, alignment.
- Save/reload.
- Verify assets and figure syntax.
- Open lightbox.

### E2E-07 - image replace failure

- Inject controlled upload failure.
- Verify original image remains.
- Retry successfully.

### E2E-08 - link card

- Paste URL.
- Convert to card.
- Simulate metadata failure/offline.
- Save/reload.
- Verify marker + ordinary link.

### E2E-09 - Details/callout disclosure

- Toggle collapsed content repeatedly.
- Assert no PUT.
- Edit title/body.
- Assert PUT and correct Markdown.

### E2E-10 - outline deep link

- Copy deep link.
- Log out.
- Navigate to link.
- Log in.
- Verify correct note/heading revealed.

### E2E-11 - math/emoji

- Insert inline/block math and emoji.
- Save/reload.
- Verify canonical text and offline rendering.

### E2E-12 - typography

- Confirm default off.
- Enable in temporary preferences.
- Verify approved prose conversion and code/path nonconversion.

### E2E-13 - open without edit

- Open every new syntax fixture.
- Wait beyond autosave.
- Navigate away.
- Assert no PUT and no disk hash/mtime/history change.

### E2E-14 - source-only safety

- Open malformed Details/figure/alignment fixture.
- Confirm Source-only reason.
- Confirm visual save cannot silently rewrite it.

### E2E-15 - stale async operation

- Start image replace/bookmark fetch.
- Switch notes.
- Complete response.
- Verify the second note is unchanged.

## 51.4 Disk verification

The E2E harness must read the actual temporary `.md` file after server-confirmed save and assert canonical syntax. UI-only assertions are insufficient.

Also verify:

- Attachment file exists.
- No pending/transient URL appears.
- File remains UTF-8/no unexpected BOM behavior.
- Front matter remains intact.
- `modified:` changes only after a real save.
- History snapshot exists for content-changing saves.
- Index/search refresh finds new content.

## 51.5 Trace and secret handling

Playwright traces, screenshots, request logs, and videos can contain note text. Keep fixtures synthetic. Never place real personal vault data or passwords in traces. Redact or disable recording for password fields as needed.

---

# 52. Vault-copy audit and compatibility report

## 52.1 Audit target

Use a read-only/copy target such as:

```text
C:\JotdexMigration\editor-ux-v2\backup
```

Never infer that this exact path exists; create it through the approved copy process on the development machine.

## 52.2 Backup requirements

Before audit:

- Stop or quiesce Jotdex if copying the live vault.
- Copy Markdown and assets without modifying source.
- Create SHA-256 manifest for every file.
- Record file size and modified time.
- Spot-restore at least one note/assets pair to a temporary location.
- Retain the 1.2.2 portable program for rollback.

## 52.3 Audit modes

Extend the migration tooling with a project-specific command or profile:

```text
npm run markdown:migrate -- audit-editor-ux --vault <copy>
npm run markdown:migrate -- stage-editor-ux --vault <copy> --out <stage>
npm run markdown:migrate -- verify-editor-ux --source <copy> --stage <stage>
```

An `apply` command is not required for this feature release. If retained generically, it must continue refusing the configured live vault without explicit override and confirmation.

## 52.4 Audit classifications

Each note receives one classification:

- `safe-visual-unchanged`
- `safe-visual-canonicalizes-on-real-edit`
- `contains-new-syntax-already`
- `source-only-existing`
- `source-only-new-conflict`
- `broken-marker`
- `unsafe-html`
- `missing-asset`
- `ambiguous-math`
- `manual-review`

## 52.5 Report outputs

Produce:

- `editor-ux-v2-audit.json`
- `editor-ux-v2-audit.csv`
- `editor-ux-v2-audit.html`
- `source-manifest.sha256`
- `stage-manifest.sha256` when staging

Reports must include paths/titles only on the local machine and must stay out of the public repo if they contain personal information.

Per note, report:

- Relative path.
- Source hash.
- Feature detections.
- Parse status.
- Source-only reason.
- Semantic comparison result.
- Potential canonical changes.
- Broken asset/link findings.
- Recommended action.

## 52.6 Staging behavior

Staging may create a proposed normalized copy, but must:

- Never overwrite source.
- Preserve folder/assets structure.
- Preserve front matter/unknown keys.
- Write a per-file transform log.
- Refuse ambiguous transformations.
- Verify every changed note through parse/serialize/parse and server render.

## 52.7 Expected release behavior

The 1.3.0 program should not require the staged tree. It should read the original vault safely, open compatible notes visually, and leave unsupported notes Source-only.

---

# 53. Manual verification checklist

Automated tests are necessary but not sufficient for interaction quality.

Use a disposable protected test vault and manually verify:

## Desktop

- Slash menu feels immediate.
- Plus button does not flicker while moving across blocks.
- Drag handle targets the intended block.
- Text selection remains stable when using bubble controls.
- Table controls are understandable.
- Image resize feels smooth and predictable.
- Link popover does not obscure text.
- Details/callout toggles are obvious.
- Outline tracks scrolling without jumping.
- Math dialog and emoji picker feel integrated.
- Toolbar remains usable and not overcrowded.

## Mobile/narrow

- Software keyboard does not hide critical menus.
- Bottom sheets close predictably.
- Long-press does not prevent text selection.
- Image controls have usable touch targets.
- Table horizontal scrolling does not trigger row/column actions.
- Outline can open/close without losing editor position.
- Pop-out behavior remains usable.

## Visual themes

- Light theme.
- Dark theme if supported/current.
- High contrast/focus visibility.
- Windows text scaling/browser zoom.
- Reduced-motion preference.

Record pass/fail and screenshots using synthetic test data only.

---

# 54. Documentation requirements

Update at minimum:

- `README.md`
- `STATUS.md`
- `CHECKLIST.md`
- `docs/changelog.md`
- `docs/vault-format.md`
- `docs/decisions/0010-editor-ux-and-jotdex-dialect-v2.md`
- `docs/decisions/editor-ux-expansion-contract.md`
- `docs/upgrading.md`
- `THIRD_PARTY_NOTICES.md`
- `src/Web/src/jotdexAiPrompt.ts`
- User-facing shortcut/help text

Documentation must cover:

- Slash and plus insertion.
- Block drag/actions.
- Bubble menu.
- Table constraints.
- Image standard vs figure format.
- Link cards.
- Details markers.
- Callout title/collapse syntax.
- Highlight/underline/sub/sup.
- Alignment markers.
- Math delimiters.
- Emoji Unicode behavior.
- Smart typography default-off policy.
- Source-only behavior.
- Downgrade considerations.
- Backup/recovery before release.

The changelog must explain why each non-obvious format choice exists, not merely list files changed.

---

# 55. Release and packaging requirements

## 55.1 Version

Use a minor release such as 1.3.0 because the project adds substantial user-facing capability and new optional Markdown dialect constructs.

## 55.2 Build gates

Run and record:

```text
dotnet build
dotnet test
cd src/Web
npm ci
npm run lint
npm run test
npm run test:editor
npm run build
npm run test:e2e
```

Use repository scripts where they already wrap these commands. Run the portable publish script and verify output.

## 55.3 Clean-machine smoke test

On a clean Windows environment without Node.js:

- Start portable Jotdex.
- Configure isolated vault.
- Set password.
- Log in.
- Open/edit/save representative note.
- Use at least slash, plus, image, table, Details, callout, outline, math, emoji.
- Restart.
- Verify content.
- Produce Share/static export.
- Verify offline behavior.

## 55.4 Upgrade test

Starting from a copy of a 1.2.2 installation:

- Upgrade program files only.
- Preserve vault/data/settings/auth/history.
- Start and log in.
- Verify old notes.
- Create new syntax note.
- Roll program back and document Source-mode/readability expectations.
- Restore 1.3.0.

## 55.5 Release artifact

The final release must include:

- Self-contained executable/program.
- Built frontend.
- Local KaTeX assets.
- Updated notices/docs.
- No Node/npm requirement at runtime.
- No development migration reports with personal paths.
- No Playwright auth state.
- No passwords/tokens.
- No source vault copy.

---

# 56. Commit strategy

Use small, reviewable commits. Recommended sequence:

1. `docs: add editor UX expansion contract and ADR skeleton`
2. `editor: add shared command registry and overlay coordinator`
3. `editor: add slash and gap insertion menus`
4. `editor: add bubble menu and safe inline marks`
5. `editor: add block drag handle and block actions`
6. `editor: add Markdown-safe table controls`
7. `editor: add figure image inspector and serializer`
8. `editor: add link popover and portable link cards`
9. `editor: add Details and callout v2 dialect`
10. `editor: add live outline and section moves`
11. `editor: add alignment, math, emoji, and safe typography`
12. `server: add render export search sanitizer parity`
13. `test: add protected E2E and vault-copy audit`
14. `release: ship portable 1.3.0`

Do not mark checklist items complete in advance. Each commit must include the tests for the behavior it introduces.

---

# 57. Prohibited shortcuts

The implementation agent must not:

- Add every feature directly inside `NoteEditor.tsx`.
- Reintroduce the abandoned community Markdown package.
- Use raw HTML as the canonical representation for everything.
- Store note content only in JSON/SQLite.
- Add paid/private Tiptap dependencies.
- Use a cloud service for editor operation.
- Rewrite the live vault to make testing easier.
- Disable password protection for E2E.
- Use the real user password in tests.
- Claim a feature is complete based only on visual appearance.
- Ignore Share/static export.
- Ignore search indexing.
- Ignore Source mode.
- Ignore undo/redo.
- Ignore mobile.
- Ignore Firefox/WebKit.
- Enable merged table cells.
- Enable arbitrary text/background colors for highlight without a format decision.
- Enable default smart typography in technical notes.
- Auto-convert dollar-delimited text to math.
- Fetch emoji/icons/fonts/scripts from a CDN.
- Delete old image assets during Replace.
- Persist temporary Details/callout open state.
- Add persistent heading IDs to every Markdown file merely for the Outline.
- Serialize or autosave on selection/scroll/menu-open events.
- Swallow disabled/failed commands without explanation.
- Mark the release complete while Source-only or migration audit changes remain unexplained.

---

# 58. Required implementation deliverables

The coding agent must provide all of the following at completion.

## Code

- Shared command registry.
- Overlay/menu system.
- Slash command feature.
- Block insertion affordance.
- Block drag/actions.
- Bubble menu.
- Table controls/validation.
- Image inspector/figure node.
- Link popover/card node.
- Details node/dialect.
- Callout v2.
- Outline v2.
- Highlight/underline/sub/sup.
- Alignment metadata.
- Math nodes/dialogs.
- Emoji picker.
- Safe typography preference.
- Server render/export/search/sanitizer changes.

## Tests

- Unit/integration tests for all IDs in this contract.
- Protected E2E suite.
- Cross-browser projects.
- Mobile project.
- Stress/race tests.
- Schema coverage.
- Round-trip fixtures.
- Backend renderer/export/search/security tests.

## Reports

- Dependency/license review.
- Vault-copy compatibility audit.
- Performance results.
- Accessibility/manual verification checklist.
- Release smoke-test result.
- Rollback result.

## Documentation

- ADR.
- Contract copy.
- Vault format.
- Changelog.
- README/help/shortcuts.
- Upgrade/rollback notes.
- AI prompt support list.
- Third-party notices.

---

# 59. Definition of Done

The project is complete only when every statement below is true.

## Architecture

- Official `@tiptap/markdown` remains the only Markdown engine.
- All Tiptap packages are exactly version-aligned.
- No paid/private runtime dependency is required.
- Persistent nodes/marks pass schema coverage.
- `NoteEditor.tsx` remains an orchestrator rather than a monolith.
- Existing codec, save, paste, attachment, and revision boundaries remain intact.

## User experience

- All 11 requested numbered capabilities are usable.
- All requested additional formatting capabilities are usable.
- Commands are discoverable by slash/plus/context menus.
- Toolbar remains usable.
- Keyboard-only operation is possible.
- Mobile does not rely on hover.
- Disabled actions explain why.

## Data safety

- Open-without-edit never rewrites a note.
- No new feature silently drops content.
- Unsupported content becomes Source-only.
- New on-disk syntax is documented.
- Runtime URLs/pending nodes never reach disk.
- Front matter and unknown fields remain intact.
- Undo/redo works for every document-changing command.
- Live vault was not bulk rewritten.

## Feature parity

- Visual editor, Source mode, server render, Share HTML, static export, search, history, and integrity checks understand the new content.
- Existing code/paste/image/task/callout behavior remains green.
- 1.2.2 block-gap behavior remains green.

## Testing

- Unit tests pass.
- Backend tests pass.
- Editor integration tests pass.
- Schema coverage passes.
- Race/stress tests pass.
- Chromium, Firefox, and WebKit E2E pass.
- Mobile E2E/manual checks pass.
- Password-protected workflows pass through normal auth.
- Disk-level assertions pass.
- Vault-copy audit has no unexplained loss.

## Security

- Authentication is not weakened.
- Unsafe links and SSRF targets are blocked.
- New HTML is strictly sanitized.
- KaTeX trust is disabled.
- No secrets appear in logs/traces/repo.
- Offline operation is verified.

## Release

- Portable build works without Node.js.
- Upgrade from 1.2.2 is verified.
- Program rollback is verified.
- New-syntax downgrade limitations are documented.
- Changelog and release notes are accurate.
- Release artifact contains no personal audit data or test credentials.

---

# 60. Final agent instructions

1. Read `AGENTS.md`, `STATUS.md`, `CHECKLIST.md`, `docs/changelog.md`, `docs/vault-format.md`, the editor reliability ADR, and official Markdown ADR before changing code.
2. Confirm the current baseline commit and package versions; do not assume the version stated in this contract remains current if the repository has advanced.
3. Copy this contract into the repository and establish checklist IDs before implementation.
4. Build the shared command/overlay infrastructure before adding individual menus.
5. Implement low-risk view interactions first, then persistent dialect features.
6. Add parser/renderer/test coverage at the same time as each persistent node or mark.
7. Keep one production Markdown engine.
8. Use existing typed insertion, paste session, attachment resolver, revision coordinator, save coordinator, and safety validator paths.
9. Test with the production extension set.
10. Test password-protected Jotdex through normal setup/login.
11. Use isolated fixtures and a copy of the real vault; do not modify the live vault.
12. Do not proceed past a work-package gate while its failures are unexplained.
13. Update documentation as behavior lands, not at the very end.
14. At completion, provide a concise implementation report listing commits, tests, vault-audit totals, Source-only totals/reasons, known limitations, release artifact, and rollback steps.

---

# 61. Reference baseline

Repository references reviewed for this contract:

- `src/Web/src/editor/extensions/createEditorExtensions.ts`
- `src/Web/src/NoteEditor.tsx`
- `src/Web/src/ImageView.tsx`
- `src/Web/src/callout.ts`
- `src/Web/src/editor/extensions/JotdexCalloutMarkdown.ts`
- `src/Web/src/editor/extensions/blockGapNavigation.ts`
- `src/Web/src/headingFold.ts`
- `src/Web/src/outline.ts`
- `src/Web/src/editor/markdown/OfficialMarkdownCodec.ts`
- `src/Web/src/editor/operations/contentInsertion.ts`
- `src/Web/src/editor/paste/PasteSessionManager.ts`
- `src/Web/e2e/auth.setup.ts`
- `src/Web/package.json`
- `docs/vault-format.md`
- `docs/changelog.md`
- `AGENTS.md`
- `STATUS.md`

Official Tiptap documentation/source areas reviewed:

- Extensions and custom extension architecture.
- Bubble Menu and Floating Menu.
- Slash-command guidance and Suggestion utility approach.
- Drag Handle.
- Table/TableKit.
- Details/DetailsSummary/DetailsContent.
- Highlight.
- Underline.
- Subscript.
- Superscript.
- Text Align.
- Mathematics/KaTeX.
- Emoji.
- Typography.
- Trailing Node.
- Official Markdown extension hooks.

Review date: 2026-09-02.

---

# 62. Summary of the intended result

After this project, Jotdex should retain the advantages it already has - ordinary files, technical search, safe code handling, attachments, backups, authentication, history, Source mode, and portability - while gaining a substantially more polished editing experience.

The user should be able to:

- Type `/` or click `+` to insert content.
- Select text and format it in place.
- Drag or keyboard-move blocks.
- Build and edit useful Markdown tables.
- Resize, replace, caption, align, and inspect images.
- Edit links and create portable bookmark cards.
- Create persistent collapsible Details.
- Highlight, underline, subscript, superscript, and align text.
- Create titled/collapsible callouts.
- Navigate and reorganize whole sections from a live Outline.
- Insert local, searchable mathematics and emoji.
- Optionally enable a conservative smart-typography mode.

None of those improvements may come at the cost of silent Markdown corruption, hidden cloud dependence, weakened password protection, or loss of file ownership.
