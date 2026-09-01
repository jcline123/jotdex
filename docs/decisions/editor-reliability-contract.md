# Jotdex Notes Editor Reliability Contract

**Repository:** `jcline123/jotdex`  
**Source baseline reviewed:** `main` at commit `9ef298b8e230ad97c98584019d569706646bdb8a`  
**Latest editor release represented by that baseline:** 1.1.23  
**Prepared:** 2026-09-01  
**Primary scope:** Visual note editing, Markdown round-trip fidelity, heading commands, rich/plain/code paste, image paste/import, autosave, history, and editor reload behavior  
**Implementation posture:** Correctness and data safety first. Preserve existing product behavior unless this contract explicitly changes an unsafe or ambiguous behavior.  
**Follow-up:** 1.2.0 replaced community `tiptap-markdown` with official `@tiptap/markdown` (ADR 0009). The reliability rules in this contract still apply.

---

## 1. Mission

Improve the Jotdex note editor so that ordinary editing feels predictable and professional while preserving the product's core promise: notes remain normal Markdown files with ordinary attachment files, and the visual editor never silently damages content.

This is not a request for a cosmetic patch or another isolated clipboard workaround. The implementation must address the common architectural boundary behind the reported failures:

1. The editor document is converted to and from Markdown through multiple code paths.
2. Async paste and upload operations modify the editor in several separate transactions.
3. Attachment metadata changes can trigger a full editor reparse even when note text did not change.
4. Block formatting commands can create structurally fragile document shapes.
5. Autosave can observe and persist intermediate states from multi-step editor operations.
6. Current tests do not exercise the actual Tiptap transaction, serializer, clipboard, reload, and browser behavior together.

The completed work must specifically eliminate these user-visible failures:

- A heading such as H3 looks correct while editing, but after leaving and reopening the note the literal Markdown prefix, such as `###`, is visible instead of a formatted heading.
- Pasted text behaves unexpectedly in a code box, leaves the code box, changes characters, adds wrappers, or creates malformed fenced Markdown.
- Pasting an image changes, flattens, merges, or exposes Markdown in text several lines before the image.
- Rich paste or image paste intermittently changes nearby content that the user did not select.
- Async image uploads insert at the wrong cursor location after the user keeps typing.
- Opening or receiving attachment metadata causes the editor to reconstruct content unnecessarily.

The implementation must also make future editor regressions much easier to detect before release.

---

## 2. Product rules that must not change

The following are non-negotiable and come from the existing Jotdex product brief, accepted ADRs, repository guidance, and current feature set.

### 2.1 Canonical storage

- The canonical note is a UTF-8 Markdown file on disk.
- Images and attachments are ordinary files in the note's sibling `.assets` directory.
- SQLite and browser state are rebuildable or temporary. They are not the only copy of note content.
- Normal images must not be permanently stored as base64 data in Markdown.
- Internal API URLs, browser object URLs, and temporary upload markers must never be written to the vault.

### 2.2 No silent content loss

- The visual editor may normalize supported Markdown after a real user edit, but it must not discard semantic content.
- Unknown or unsupported content must be preserved, represented by a protected raw node, or force Source mode with a clear explanation.
- A parse or serialization failure must not overwrite the last good Markdown file.
- Opening and closing a note without an edit must not rewrite the file or create a history snapshot.

### 2.3 Existing features that must remain available

Do not remove or materially weaken any of these features while fixing reliability:

- Visual and Source modes
- H1 through H6 support, including the existing toolbar behavior
- Bold, italic, underline, strike, highlight, text color, and font size
- Bullet, numbered, and task lists
- Tables
- Block quotes and callouts
- Horizontal rules
- Links and wiki-link completion
- Heading folding and outline navigation
- Code blocks, language selection, inline editing, CodeMirror Edit dialog, diagnostics, snippets, copy, and paste-as-code
- Smart paste, match-formatting paste, plain paste, code paste, and preserve-page paste
- Pasted screenshots, local files, data images, remote images, and attachment links
- Autosave, ETag conflict handling, history, restore, and unload save protection
- Pop-out editing and mobile editing
- HTML sidecars and static export

### 2.4 Security and privacy

- Keep the current sanitizer, CSP, attachment path containment, and SSRF protections.
- Never log note bodies, clipboard contents, image bytes, passwords, cookies, or attachment contents.
- Diagnostics may log operation identifiers, node types, node counts, positions, timing, validation codes, note ID, and revision numbers.

---

## 3. Source-level findings and required interpretation

### 3.1 Confidence labels

Use these labels when documenting implementation results:

- **Confirmed:** Directly demonstrated by the current source or a matching dependency defect.
- **Strongly indicated:** The source contains the complete failure mechanism, but a live browser reproduction is still required.
- **Possible contributor:** Plausible and must be tested, but should not be presented as the sole cause without reproduction.

### 3.2 Confirmed: block image serialization can fuse with a following heading

Jotdex configures the Tiptap image extension as a block node. The community `tiptap-markdown` package delegates image serialization to ProseMirror's default image serializer, which writes image syntax but does not close a block. The same package delegates headings to the default heading serializer.

The dependency has a matching reported defect in which this document:

```text
image node
heading node
```

is serialized as:

```markdown
![](image-url)## Subtitle
```

or the H3 equivalent:

```markdown
![](image-url)### Subtitle
```

On the next parse, the heading marker is no longer at the start of a Markdown block, so it appears as literal text. This precisely matches the reported symptom where the heading looked correct while editing but reopened with Markdown characters visible.

**Required response:** Jotdex must own the block-image Markdown serializer and explicitly close/separate block images. Do not wait for the community dependency to fix this.

### 3.3 Confirmed: partial heading conversion uses a structurally fragile transaction

`headingSelection.ts` currently handles a partial selection in one text block by:

1. Cutting the selected inline content.
2. Deleting only the selected range.
3. Creating a block-level heading node from that inline content.
4. Inserting the block node at the former inline position.

That relies on ProseMirror's insertion fitting to split or restructure the containing block correctly. It is not a deterministic block transformation. It can create unexpected empty paragraphs, invalid nesting, altered neighboring blocks, or document shapes that serialize differently after reload.

**Required response:** Partial heading conversion must replace the entire containing text block with an explicitly constructed fragment containing the left remainder, heading, and right remainder. A block node must never be inserted directly at an arbitrary inline position.

### 3.4 Confirmed: rich image paste is currently a series of independently serializable transactions

The current rich-paste path performs the following sequence:

1. Sanitize clipboard HTML.
2. Replace data images with temporary `https://paste.invalid/...` URLs.
3. Insert the marked-up HTML into the document.
4. Allow the normal Tiptap `onUpdate` path to serialize the entire document.
5. Upload each data image.
6. Scan the full document for the temporary source and replace matching image nodes.
7. Serialize again after each replacement.
8. Import each remote image.
9. Scan the full document for each remote source and replace matching nodes.
10. Serialize again after each replacement.
11. Emit another final serialization.
12. Update attachment metadata.

Every one of those document transactions can be observed by the parent draft and autosave system. Temporary states are therefore eligible to become the parent draft, enter an ETag race, or be reparsed before the paste is complete.

**Required response:** Rich paste with assets must become one logical paste session with stable placeholders, explicit pending state, controlled serialization, and one committed result.

### 3.5 Confirmed: attachment-list changes can reload the entire editor document

The editor reload effect currently calls `setContent()` when any of the following changes:

- `contentEpoch`
- Incoming Markdown differs from the last emitted Markdown
- The serialized attachment list differs from the last attachment list

Attachment upload does not change the note ETag because the ETag is a SHA-256 hash of the Markdown text. It only changes the attachment inventory. Reconstructing the entire ProseMirror document for a metadata-only change is unnecessary and dangerous.

This is especially risky during image paste because attachment updates occur while the editor is also inserting or resolving images. A stale React render or an older draft can be reparsed over the newer in-memory editor state.

**Required response:** Attachment metadata updates must update an attachment resolver or plugin state only. They must not call `setContent()` and must not increment the document epoch.

### 3.6 Confirmed: direct image upload does not preserve the original insertion point

The direct file-paste/upload path awaits the upload and only then calls `editor.chain().focus().setImage(...)`. That uses the selection that exists when the upload finishes, not necessarily the selection where the user pasted the file.

The existing product brief explicitly requires that image upload not lose the editor selection.

**Required response:** Capture a mapped bookmark or insert a stable placeholder synchronously at the original paste position. The finished image must resolve that exact placeholder even if the user moves the cursor or continues typing.

### 3.7 Confirmed: replacing images by source URL can affect content outside the paste

`replaceImageSrc` scans the full document and changes every image whose `src` matches the supplied value. During remote-image localization, that can replace an image that already existed elsewhere in the note if it uses the same remote URL. Temporary markers are unique, but external URLs are not guaranteed unique to the newly pasted fragment.

**Required response:** Resolve newly pasted images by a unique paste-session and placeholder ID, never by a global source-string search.

### 3.8 Confirmed: code paste has improved, but the behavior is implemented in several places

Version 1.1.23 added an important fix: when the selection is inside an active code block, paste now uses `Transaction.insertText` so multiline text stays inside the block. Preserve that behavior.

However, code clipboard behavior is still distributed across:

- A ProseMirror plugin added by the code-block extension
- The top-level editor `handlePaste`
- Document-level capture listeners for code copy/cut
- The toolbar's Paste code action
- Paste-mode handling
- CodeMirror Edit dialog and snippet insertion paths

Multiple interceptors make precedence and browser differences difficult to reason about. Some helper paths also call `.trim()` for detection or wrapper stripping, which can damage code consisting of leading/trailing blank lines or whitespace-only text.

**Required response:** All code paste entry points must use one shared command and one set of exact-character rules.

### 3.9 Confirmed: full Markdown serialization occurs on every editor update

The editor's `onUpdate` callback serializes the full ProseMirror document to Markdown and sends it to the parent on each update transaction. The parent then debounces the server save.

This means the expensive and failure-sensitive serialization step happens much more often than the actual save. It also means metadata, placeholder, and normalization transactions can produce draft Markdown even when they should not represent a committed user document.

**Required response:** Separate “the editor is dirty” from “a validated Markdown revision is ready to save.” Serialize at controlled commit/debounce boundaries and immediately for explicit Save, not blindly for every transaction.

### 3.10 Confirmed: frontend and backend document sameness rules differ

Frontend `sameMarkdown()` currently normalizes line endings, removes trailing whitespace, collapses three or more blank lines to two, strips leading blank lines, normalizes the end, and ignores `modified:`.

Backend `SameDocument()` normalizes line endings, trims the end, and ignores `modified:`, but does not collapse interior blank lines or trailing spaces on each line.

That mismatch can cause one side to treat a document as unchanged while the other side considers it changed.

**Required response:** Define shared test vectors and align the comparison rules. Dirty detection, conflict suppression, save acknowledgment, and open-without-edit behavior must not use subtly different definitions.

### 3.11 Confirmed: current formatting check cannot catch these defects

The existing remark-lint check reports final newline, consecutive blank lines, heading increments, and missing fenced-code language flags. It does not check:

- Block image immediately followed by a heading marker
- A block marker fused to preceding content
- Temporary `paste.invalid`, `blob:`, `data:`, or internal API URLs
- A semantic mismatch after serialize and parse
- Unintended changes outside the user's operation
- Placeholder nodes that should not be saved
- Unclosed or structurally altered code fences caused by editor transactions

**Required response:** Keep remark-lint as a user-facing style check, but add a separate internal save-safety validator.

### 3.12 Confirmed: the current frontend lacks an editor test harness

The repository has strong backend and vault fixtures, but the web package does not currently define Vitest or Playwright scripts. Existing round-trip fixtures do not drive the real Tiptap editor, selection commands, clipboard events, async uploads, React prop updates, `setContent`, autosave, or browser-specific clipboard behavior.

**Required response:** Add unit/integration tests for the editor and browser-level Playwright tests. The product brief already identifies Vitest and Playwright as the intended tools.

### 3.13 Confirmed: the Markdown dependency is not a safe long-term foundation by itself

Jotdex currently uses community package `tiptap-markdown` 0.9.0 with Tiptap 3. The package maintainer now recommends the official Tiptap Markdown extension for Tiptap 3 and indicates that current issues and pull requests are not expected to be addressed. The official extension is still an evolving/beta surface and custom Jotdex nodes require deliberate handlers.

**Required response:** Do not combine a dependency migration with the first reliability fix. First create a Jotdex-owned codec boundary and test corpus. Then run a separate, evidence-based migration spike. Jotdex must own its serialization contract regardless of which package performs the lower-level conversion.

### 3.14 Strongly indicated: the “five lines above the image” corruption is a transaction/reload problem

The exact live reproduction still needs to be captured, but the source contains a complete high-risk sequence:

- Rich paste changes the whole document several times.
- Every change is serialized.
- Attachment updates can reparse the whole editor.
- A React state update can lag an editor transaction.
- Image resolution scans the full document.
- Autosave can observe intermediate Markdown.
- The dependency has known block-boundary serialization defects.

The important point is that “five lines” is probably not a meaningful fixed distance. It is more likely the nearest blocks affected when the editor reparses or fits a malformed slice. Tests must therefore use sentinels around the paste location rather than encoding a five-line special case.

---

## 4. Non-negotiable editor invariants

These invariants are the core acceptance contract. Implement them as code-level assertions and automated tests where practical.

### 4.1 Persisted Markdown invariants

A save must be rejected before the server write if the body contains any transient editor artifact, including:

- `https://paste.invalid/`
- `blob:` URLs
- `data:image/` generated by a normal image paste
- `/api/attachments/` display URLs
- Internal upload or placeholder identifiers
- A temporary React or ProseMirror node representation

Exceptions must be explicit and limited to intentionally preserved raw source content. A generic normal image node may not use those values as its canonical persisted source.

### 4.2 Block-boundary invariants

- Every block image must be separated correctly from a following block.
- Heading markers must begin at a valid Markdown block start.
- List markers, blockquote markers, task markers, fenced-code delimiters, table rows, and horizontal rules must not be fused to preceding inline output.
- Fenced code blocks must be balanced and preserve their language and exact content.
- A serialization validator must detect suspicious fused boundaries and fail safe.

### 4.3 Semantic round-trip invariant

For all editor-supported nodes:

```text
current ProseMirror document
    -> serialize to Markdown
    -> parse that Markdown with the production parser
    -> normalize only documented transient/canonical attributes
```

must produce a semantically equivalent document.

The semantic comparator must check:

- Node type and order
- Text content
- Meaningful node attributes
- Marks and mark attributes
- List/task state
- Heading level
- Code language and code text
- Table dimensions, headers, and cell content
- Image canonical source, alt, and title
- Link target and title
- Callout type and content
- Raw/protected content identity

Allowed canonical differences must be enumerated in one policy file. Do not scatter `if` statements throughout tests to excuse failures.

### 4.4 Locality invariant

An operation may only change the blocks it is intended to change.

For paste and formatting tests, create semantic fingerprints for at least five blocks before and five blocks after the operation. After save and reopen, all out-of-scope blocks must retain the same semantic fingerprints.

This is the direct regression guard for the reported “lines above the picture changed” behavior.

### 4.5 Transaction invariant

A user-visible operation must have one stable operation ID and one final serializable result.

Examples of one operation:

- Apply H3 to a selection
- Paste rich text containing three images
- Paste a screenshot
- Paste as code
- Insert a snippet
- Resolve all uploads belonging to one paste

Internal async steps may use multiple ProseMirror transactions, but those transactions must carry the same operation ID, must not be individually eligible for autosave when transient, and must not create multiple history versions unless the user makes independent edits between them.

### 4.6 Reload invariant

`setContent()` may be used only for an explicit document replacement event:

- Initial note load
- User-selected reload from disk
- User-selected history restore
- Explicit Source-to-Visual mode conversion
- Explicit preserve-page response that intentionally replaces the document and passes revision checks
- A confirmed external version adoption

It must not run because:

- An attachment was added
- An attachment list changed
- Upload progress changed
- An image display URL became available
- A save response returned the same Markdown
- An ETag changed while the local document did not

### 4.7 Save acknowledgment invariant

A save response may mark the editor `Saved` only if it acknowledges the latest serializable revision for the same note session.

An older response may update server metadata such as an ETag, but it must not:

- Replace a newer local document
- Reset the dirty state of newer edits
- Increment the editor epoch
- Trigger `setContent()`
- Change the selected note

### 4.8 Open-without-edit invariant

For every supported fixture:

1. Load note.
2. Wait for editor initialization.
3. Make no edit.
4. Close or switch notes.

Expected:

- No PUT request
- No file rewrite
- No history snapshot
- No modified timestamp change
- No draft normalization emitted as a user edit

---

## 5. User-visible behavior contract

### 5.1 Headings

#### Caret inside a top-level paragraph

- Clicking H1/H2/H3 toggles the entire current text block to that heading level.
- Clicking the active level again returns it to a paragraph.
- Save and reopen must preserve the result.

#### Entire top-level paragraph selected

- Same behavior as the caret case.
- No extra empty paragraph may appear before or after the heading.

#### Partial selection within one top-level paragraph

Given:

```text
Alpha beta gamma delta
```

and selection `beta gamma`, applying H3 must produce this document structure:

```text
paragraph: Alpha 
heading level 3: beta gamma
paragraph:  delta
```

Rules:

- Construct the three sibling blocks explicitly by replacing the entire original text block.
- Omit an empty left or right block unless an empty paragraph is intentionally required for cursor placement.
- Preserve marks that are valid inside a heading.
- Do not duplicate or lose whitespace at the split boundaries.
- Place the selection or caret inside the new heading after the transaction.
- One Undo must restore the original single paragraph.
- One Redo must restore the split structure.
- Save and reopen must produce the same three semantic blocks.

#### Selection spanning multiple top-level text blocks

- Apply the requested heading level only to eligible selected text blocks.
- Preserve order.
- Do not convert code blocks, images, tables, horizontal rules, or raw nodes.
- Toggling an already uniform heading selection returns eligible blocks to paragraphs.
- Mixed selections must have deterministic behavior documented in tests.

#### Nested contexts

Do not guess at Markdown behavior inside lists, task items, table cells, callouts, or blockquotes.

The implementation must first capture current behavior and then use this rule:

- Preserve an existing nested behavior only if it passes semantic round-trip tests.
- If a partial heading is not safely representable in that context, do not mutate the document. Disable the command for that selection and show a concise explanation.
- Do not silently lift content out of a list or table unless a separate explicit command is designed and tested.

### 5.2 Normal image paste and drop

- Insert a visible pending placeholder immediately at the original paste/drop position.
- The placeholder must have a unique `pasteSessionId` and `uploadId`.
- Moving the cursor or typing elsewhere must not move the final image.
- The image must replace only its matching placeholder.
- The document must not be reparsed when the attachment inventory updates.
- The pending state must not be serialized to Markdown.
- Autosave may show a distinct `Uploading` or `Finishing paste` state while a placeholder exists.
- On success, save one final Markdown image reference using the relative vault path.
- On failure, keep existing note content safe and show Retry and Remove controls.
- Do not silently omit a failed image while displaying the note as Saved.
- Switching notes while uploads are pending must either finish/cancel safely or display a clear confirmation. An upload result must never be applied to another note.

### 5.3 Rich paste with data images and remote images

- Sanitize the clipboard HTML once.
- Parse the sanitized content once into a controlled slice or JSON structure.
- Assign a stable ID to every image belonging to the pasted fragment.
- Insert the prose structure and placeholders as one logical paste transaction.
- Preserve the relative order of text, lists, tables, code, and images.
- Upload or import images with bounded concurrency.
- Resolve by placeholder ID, not source URL.
- Pre-existing images elsewhere in the note must not change, even if they share a source URL with a newly pasted image.
- Do not emit or autosave temporary image URLs.
- Do not perform one full-document `setContent()` per attachment.
- Do not create a history snapshot for each individual image resolution.

### 5.4 Smart paste structure

Smart paste must preserve supported semantic structure while removing unsafe or irrelevant page chrome.

Required structure cases:

- Headings H1-H6
- Paragraphs
- Bold and italic
- Links
- Ordered and unordered lists
- Task lists when clearly represented
- Block quotes
- Fenced/preformatted code
- Tables that can be represented safely
- Images
- Simple inline color/font-size styles already supported by Jotdex

If a pasted table or complex fragment cannot pass the production round-trip check, preserve it as sanitized raw HTML or offer preserve-page behavior. Do not show a correct-looking table in the editor and then serialize it as `[table]` or an incomplete table.

### 5.5 Plain paste

- Insert clipboard plain text.
- Preserve line order.
- Normalize Windows CRLF and old Mac CR to LF.
- Do not interpret Markdown.
- Do not insert HTML structure.
- Do not remove leading spaces unless the destination command explicitly requires it.

### 5.6 Paste into an active code block

All entry points must share these exact rules:

- Insert text with a ProseMirror text transaction inside the current code block.
- Never parse clipboard HTML as editor structure.
- Preserve tabs, spaces, blank lines, backticks, quotes, backslashes, angle brackets, ampersands, and Unicode characters.
- Normalize only line endings to LF.
- Preserve leading and trailing blank lines.
- Preserve whitespace-only clipboard content.
- Replace the current code selection if one exists.
- Multiline paste must remain in the same code block.
- It must not create sibling paragraphs or sibling code blocks.
- Save and reopen must preserve exact code text and fence language.

### 5.7 Paste as code outside a code block

- Capture the current selection before any async clipboard work.
- Replace the selected range with exactly one code block.
- Use the shared clipboard-to-plain-code conversion.
- Do not trim the code.
- Choose a language only when explicitly supplied by the user or a trusted feature. Do not infer and silently rewrite.
- One Undo restores the previous selection/content.

### 5.8 Code copy and cut

- Copy from a code box must place exact plain text in `text/plain`.
- Browser `StartFragment` wrappers and contenteditable spans must not appear in copied text.
- Cut must delete only the selected code text.
- Leading/trailing whitespace in the selection must be preserved.
- Copy behavior must be tested in Chromium, Firefox, and WebKit.

### 5.9 Source and Visual mode

- Switching Source to Visual must parse through the same production codec and validation path as initial load.
- Switching Visual to Source must use the same validated serializer as autosave.
- If Visual serialization fails, keep the visual document intact, do not save malformed Markdown, and display a clear recovery banner.
- The banner must offer at least: copy diagnostic summary, return to Source when a safe source representation exists, and continue editing without overwriting disk.
- Do not expose internal stack traces to the user.

### 5.10 Autosave and history

- Preserve the accepted 800-1200 ms idle autosave behavior for committed edits.
- Distinguish `Editing`, `Finishing paste`, `Saving`, `Saved`, `Conflict`, and `Error` as needed.
- `Saved` appears only after atomic server confirmation for the latest serializable revision.
- A slow image paste must not save temporary placeholders.
- A single completed paste should normally create at most one content save and one history snapshot.
- User edits made after the paste but before the server response must remain dirty and must not be overwritten.
- Ctrl/Cmd+S must flush the latest serializable editor revision and save immediately. If a paste is pending, it must explain why the document cannot yet be finalized rather than saving an incomplete version.

---

## 6. Required target architecture

The names below are suggestions. Equivalent names are acceptable, but the responsibilities and boundaries are mandatory.

### 6.1 Data flow

Replace the current implicit flow with an explicit one:

```text
Vault Markdown
    -> EditorMarkdownCodec.parse()
    -> ProseMirror document containing canonical persisted attributes
    -> user/editor transactions with operation metadata
    -> EditorRevisionCoordinator marks dirty
    -> controlled EditorMarkdownCodec.serialize()
    -> SaveSafetyValidator
    -> semantic round-trip verification
    -> validated Markdown revision
    -> SaveCoordinator
    -> server atomic write + ETag
```

Attachment metadata and display URL resolution must travel alongside this flow, not through document replacement:

```text
attachment inventory
    -> AttachmentResolver plugin/context
    -> Image NodeView display URL
```

The canonical image target stored in the ProseMirror document should remain a vault-relative or external source, not an internal API display URL.

### 6.2 `EditorMarkdownCodec`

Create one Jotdex-owned adapter used by:

- Initial visual load
- Visual-to-Source conversion
- Source-to-Visual conversion
- Autosave serialization
- Ctrl/Cmd+S
- Preserve-page document replacement
- Editor round-trip tests
- Optional repair preview

Suggested interface:

```ts
export type EditorDiagnostic = {
  code: string
  severity: 'warning' | 'error'
  message: string
  nodePath?: number[]
  markdownLine?: number
  operationId?: string
}

export type ParseResult = {
  ok: boolean
  doc?: JSONContent
  diagnostics: EditorDiagnostic[]
  forcedSourceReason?: string
}

export type SerializeResult = {
  ok: boolean
  markdown?: string
  diagnostics: EditorDiagnostic[]
  semanticFingerprint?: string
}

export interface EditorMarkdownCodec {
  parse(markdownBody: string): ParseResult
  serialize(doc: ProseMirrorNode): SerializeResult
  compareSemantic(a: ProseMirrorNode, b: ProseMirrorNode): SemanticComparison
}
```

Requirements:

- Asset display URL mapping must not be a global string-replacement pass over Markdown.
- The codec must own custom handlers for Jotdex-specific nodes and marks.
- The codec must explicitly handle block images.
- The codec must expose diagnostics rather than throwing raw errors into React.
- Unsupported content must fail safe.
- The same codec instance/configuration must be used in tests and production.

### 6.3 Jotdex-owned block image serializer

Keep images visually block-level. Override the image Markdown storage/serializer so a block image closes its Markdown block before the next node.

Implementation guidance:

- Reuse the proven escaping logic from the underlying image serializer.
- After writing a non-inline image, call the serializer's block-close mechanism exactly once.
- Do not add a regex-only patch that blindly inserts newlines after every `)` character.
- Add a defensive post-serialization validator for fused image/heading output.
- Test an image at the beginning, middle, and end of a note; before and after every major block type.

Minimum exact regression:

```markdown
# Title

![alt](Note.assets/image.png)

### Subtitle
```

must never serialize as:

```markdown
![alt](Note.assets/image.png)### Subtitle
```

### 6.4 Canonical asset representation

Preferred design:

- `node.attrs.src` remains the canonical persisted source, such as `Note%20Name.assets/image.png` or a safe external URL.
- `node.attrs.alt` and `node.attrs.title` remain canonical.
- A NodeView resolver converts the canonical source to `/api/attachments/{id}` for display without mutating the document.
- Pending upload state is represented by a dedicated non-persistable placeholder node or plugin decoration with `uploadId`, not by placing a temporary URL in `src`.

If a dedicated image attribute such as `vaultPath` is used instead, the custom serializer must always prefer it and must reject a transient `src`. Pick one design and document it. Do not keep both as loosely synchronized strings.

### 6.5 `PasteSessionManager`

Create a single manager for smart paste, image paste, file drop, and remote image localization initiated by paste.

Suggested state machine:

```text
Created
  -> Parsed
  -> PlaceholdersInserted
  -> Uploading
  -> Resolving
  -> Validating
  -> Committed

Failure branches:
  -> NeedsUserAction
  -> Cancelled
```

Each session must store:

- `pasteSessionId`
- `noteId`
- `noteSessionId` or editor epoch
- starting editor revision
- original selection bookmark or placeholder IDs
- paste mode
- sanitized structure
- uploads/imports and their status
- abort controllers
- operation diagnostics

Rules:

- Results are ignored if note ID or note session no longer matches.
- Results resolve by `uploadId`/placeholder identity.
- Upload completion order must not change document order.
- Pending placeholders suppress serialization of an incomplete result.
- User typing outside the placeholders remains allowed and maps their positions normally.
- Undoing the paste cancels or invalidates unresolved uploads.
- Redo creates or reuses a safe logical paste state without duplicating attachments unexpectedly.
- A failed upload does not corrupt or replace surrounding text.

### 6.6 Operation metadata on transactions

Use ProseMirror transaction metadata for auditability and save control.

Suggested fields:

```ts
export type JotdexOperationMeta = {
  operationId: string
  kind:
    | 'typing'
    | 'heading'
    | 'paste-rich'
    | 'paste-plain'
    | 'paste-code'
    | 'image-resolve'
    | 'attachment-metadata'
    | 'external-load'
    | 'history-restore'
    | 'source-convert'
  serializable: boolean
  commitBoundary: boolean
  suppressAutosave?: boolean
  pasteSessionId?: string
}
```

The implementation does not need to serialize this metadata. It exists to coordinate editor behavior and diagnostics.

### 6.7 `EditorRevisionCoordinator`

Track at least:

- `editorRevision`: increments on meaningful document changes
- `latestSerializableRevision`
- `latestValidatedRevision`
- `lastSavedRevision`
- `noteSessionId`: changes when the selected note/document is replaced
- pending operation IDs

Responsibilities:

- Emit an immediate dirty event without serializing the full document.
- Debounce serialization for normal typing.
- Wait for transient paste operations before creating a serializable revision.
- Flush immediately for Ctrl/Cmd+S.
- Ignore selection-only and metadata-only transactions.
- Send a validated Markdown revision to the parent with its revision number.

Suggested callbacks:

```ts
onDirty({ revision, operationId })
onValidatedChange({ markdown, revision, operationId })
onValidationError({ revision, diagnostics })
```

Do not use a single `onChange(markdown)` callback for every editor transaction.

### 6.8 `SaveCoordinator`

Move save ordering into an explicit coordinator instead of relying on interlocking React refs and timers.

Required behavior:

- At most one save request in flight per note session.
- Keep only the newest queued validated revision.
- A request records note ID, note session ID, revision, Markdown, and ETag sent.
- A response applies only to the matching note session.
- A response marks Saved only when its revision is still the newest validated revision.
- If newer edits exist, update the ETag as appropriate and immediately or normally schedule the newer revision without clearing dirty state.
- Do not retry a real external conflict as last-write-wins without surfacing it.
- Distinguish an overlapping local autosave from an actual disk change by revision/request identity, not only by broad Markdown normalization.
- Preserve explicit overwrite as an intentional user action.

### 6.9 Typed server events

Replace ambiguous metadata callbacks such as an object with optional `markdown`, `attachments`, and `etag` fields with a discriminated union.

Example:

```ts
type NoteServerEvent =
  | { kind: 'attachments-updated'; attachments: Attachment[] }
  | { kind: 'etag-confirmed'; etag: string; revision: number }
  | { kind: 'replace-document'; markdown: string; etag: string; reason: ReplaceReason }
```

Only `replace-document` may trigger `setContent()`, and only after revision/session checks.

### 6.10 Save-safety validator

Create a validator separate from remark-lint.

It must check at minimum:

- No transient/internal asset URL
- No pending placeholder representation
- No fused image followed by heading/list/blockquote/fence/table marker
- Balanced fenced code blocks
- Production parse succeeds
- Semantic parse-back equals the current supported editor document
- No unsupported node was dropped
- No empty serializer fallback such as `[table]` for a supported node

On validation failure:

- Do not call the note PUT endpoint.
- Keep the current visual document in memory.
- Keep the last good file untouched.
- Display a non-destructive error banner.
- Log diagnostic codes and structure only.

### 6.11 Document comparison policy

Create shared JSON test vectors used by TypeScript and C# for document sameness.

Separate two concepts:

1. **Exact save equivalence:** line endings normalized and server-managed `modified:` ignored. Do not collapse meaningful interior source formatting without an explicit rule.
2. **Semantic editor equivalence:** AST/document comparison used to prove round-trip fidelity.

Do not use the current broad frontend `normalizeMarkdown()` as the only basis for save state and conflict decisions.

---

## 7. Required work packages

Implement in this order. Each work package must leave the repository buildable and must add tests before changing the next boundary.

### WP0 — Reproduction harness and baseline capture

#### Deliverables

- Add Vitest with an appropriate DOM environment for editor tests.
- Add Playwright with Chromium, Firefox, and WebKit projects.
- Add a test-only editor host that mounts the real `NoteEditor` with fake note APIs and controllable upload delays.
- Capture the current behavior of the reported failures before changing implementation.
- Add a fixture named similar to `editor-boundary-torture.md` containing headings, paragraphs, code, list, task list, table, callout, image, raw HTML, styles, links, and wiki links.
- Add a diagnostic utility that prints document JSON and serialized Markdown only in test output, never production logs.

#### Mandatory reproductions

1. Image immediately followed by H2 and H3.
2. Partial paragraph selection converted to H3.
3. Rich paste containing text and one data image with delayed upload.
4. Rich paste containing multiple images completing out of order.
5. Attachment metadata update during a pending paste.
6. Active code block multiline paste.
7. Paste code containing leading/trailing blank lines and whitespace-only text.
8. Cursor movement while direct image upload is pending.

#### Gate

No production behavior change is accepted until the tests can demonstrate at least the exact image/heading defect and the metadata-triggered `setContent()` path. If one user report cannot be reproduced, retain it as a test hypothesis and add operation-level instrumentation before claiming it fixed.

### WP1 — Central codec and exact block-image fix

#### Deliverables

- Add `EditorMarkdownCodec`.
- Add Jotdex-owned block image Markdown serialization.
- Route initial load and visual serialization through the codec.
- Add save-safety validation for transient URLs and fused block markers.
- Add semantic round-trip comparison for core nodes.

#### Gate

- Image followed immediately by H1-H6 survives 100 serialize/parse cycles.
- Image before paragraph, list, task list, quote, code fence, table, horizontal rule, callout, and another image remains structurally correct.
- Existing round-trip fixtures remain semantically intact.
- Open-without-edit emits no change.

### WP2 — Deterministic heading commands

#### Deliverables

- Rewrite partial single-block heading transformation using whole-block replacement.
- Preserve full-block and multi-block behavior.
- Add clear handling for nested contexts.
- Add selection restoration and Undo/Redo tests.

#### Gate

- All heading tests pass after save and reopen.
- No neighboring sentinel block changes.
- No extra empty paragraphs.
- No raw `#`, `##`, or `###` appears because of serializer adjacency.
- One Undo restores the exact prior document semantics.

### WP3 — Attachment resolver and removal of metadata reloads

#### Deliverables

- Keep canonical asset paths in the editor document.
- Resolve display URLs in the NodeView/plugin layer.
- Remove attachment-list changes from the full document reload dependency.
- Replace ambiguous `onNoteMeta` document/metadata behavior with typed events.
- Add a test spy proving metadata-only updates do not call `setContent()`.

#### Gate

- Uploading an attachment changes the attachment inventory without replacing the document.
- Caret, selection, undo history, folded headings, and scroll position remain stable after metadata updates.
- ETag remains tied to Markdown content as in the backend.

### WP4 — Transactional paste sessions

#### Deliverables

- Add `PasteSessionManager` and stable placeholder IDs.
- Route direct image paste, image drop, data-image rich paste, and remote-image rich paste through it.
- Add cancellation/session checks.
- Add pending/failure UI.
- Suppress serialization while non-persistable placeholders remain.
- Remove global source-URL replacement for newly pasted images.

#### Gate

- Images always resolve at their original positions.
- Out-of-order upload completion preserves paste order.
- Moving the cursor or typing does not move images.
- Switching notes does not apply late upload results to the new note.
- Existing same-source images outside the paste remain unchanged.
- No temporary URL reaches a PUT request.
- A completed multi-image paste normally produces one content save and one history snapshot.

### WP5 — Consolidated code clipboard behavior

#### Deliverables

- Create one shared code clipboard conversion and insertion command.
- Route active-code paste, paste-as-code mode, toolbar Paste code, snippets, and Edit-dialog insertion through shared primitives where their behavior overlaps.
- Remove or narrow redundant interceptors after browser tests prove precedence.
- Stop trimming code payloads.
- Keep known wrapper removal narrowly targeted to browser clipboard pollution.

#### Gate

Exact content survives copy/paste/save/reopen for:

- Tabs
- Spaces
- Leading and trailing blank lines
- Whitespace-only text
- Backticks and nested fences
- HTML/XML
- JSON
- PowerShell with backticks and backslashes
- Ampersands and entities
- Unicode
- Windows and Unix line endings

Multiline paste in an active block never creates sibling prose.

### WP6 — Revision-aware serialization and autosave

#### Deliverables

- Add dirty-versus-validated-change callbacks.
- Stop full Markdown serialization for selection-only and metadata-only transactions.
- Add explicit editor revision and note session tracking.
- Add a SaveCoordinator with ordered request acknowledgment.
- Align frontend/backend exact document comparison test vectors.
- Ensure Ctrl/Cmd+S flushes the current validated revision.

#### Gate

- Slow save response cannot clear newer dirty edits.
- Older save response cannot replace editor content.
- Two rapid editing bursts save the newest content in order.
- A true external edit produces conflict UX rather than silent overwrite.
- Open-without-edit produces no PUT.
- No hot save loop occurs from cosmetic serialization.

### WP7 — Full browser and product regression suite

#### Deliverables

- Playwright matrix for Chromium, Firefox, and WebKit.
- At least one Windows packaged-app smoke test documented for Edge/Chrome.
- Safari/WebKit clipboard and image tests.
- Mobile viewport tests for toolbar and code paste.
- Pop-out editor tests for note session isolation.
- Existing backend tests and publish probes remain green.

#### Gate

All tests in Section 8 pass, production frontend builds, .NET tests pass, and the packaged app opens the representative vault without rewriting notes.

### WP8 — Dependency migration spike, separate from the reliability release

#### Deliverables

- Add an ADR comparing community `tiptap-markdown` with official `@tiptap/markdown` against the Jotdex fixture corpus.
- Run both codecs in test/shadow mode only.
- Document every difference in headings, lists, tasks, tables, code, images, HTML marks, callouts, wiki links, folds, and unsupported nodes.
- Implement custom handlers required by Jotdex before considering a switch.

#### Gate for migration

Do not switch production until:

- All agreed semantic fixtures pass.
- No unsupported Jotdex feature is dropped.
- Open-without-edit remains no-op.
- Performance is no worse than the current validated codec by more than an explicitly accepted threshold.
- The migration changes no vault file merely because the app upgraded.

### WP9 — Optional repair scanner for already-corrupted notes

This is a separate feature and must not run automatically.

#### Deliverables

- Report-only vault scan for unambiguous patterns such as a Markdown image immediately fused to a heading marker.
- Preview diff.
- History snapshot before repair.
- Explicit per-note or batch confirmation.
- Safe parsing before and after the proposed change.

#### Restrictions

- Do not broadly normalize the vault.
- Do not guess at ambiguous `)#` text inside prose or code.
- Do not modify files without user approval.
- Do not combine this with the editor reliability PR.

---

## 8. Mandatory automated acceptance matrix

Each row must test editor state immediately after the operation, emitted Markdown, server-save payload when applicable, and state after close/reopen.

| ID | Scenario | Required result |
|---|---|---|
| H-01 | Caret in paragraph, click H3 | Entire paragraph becomes H3; survives reopen |
| H-02 | Full paragraph selected, click H3 | H3 with no extra paragraph; survives reopen |
| H-03 | Middle words selected, click H3 | Explicit paragraph/H3/paragraph split; no text loss |
| H-04 | Selection starts/ends with whitespace | Whitespace preserved at correct boundaries |
| H-05 | Partial bold/italic text to H3 | Valid marks preserved; invalid marks handled explicitly |
| H-06 | Multi-paragraph selection to H3 | Eligible blocks convert only; order preserved |
| H-07 | Triple-click selection overhang | Following block is not changed |
| H-08 | H3 immediately after local image | Blank block separation in Markdown; formatted after reopen |
| H-09 | H3 immediately after remote image | Same result as local image |
| H-10 | H3 immediately before image | Both nodes survive unchanged |
| H-11 | Undo partial heading | Original single paragraph restored in one Undo |
| H-12 | Redo partial heading | Split heading structure restored |
| IMG-01 | Paste screenshot and do nothing | Pending UI, then local relative image; one final save |
| IMG-02 | Paste screenshot, move cursor, type elsewhere | Image remains at original paste location |
| IMG-03 | Paste two screenshots with delayed reverse completion | Original image order preserved |
| IMG-04 | Paste rich text with one data image | Text and image order preserved; no marker persisted |
| IMG-05 | Paste rich text with five images | One logical operation; no neighboring block changes |
| IMG-06 | Paste remote image already present above | Only newly pasted placeholder localizes |
| IMG-07 | Attachment metadata refresh | No `setContent`; selection and undo stack preserved |
| IMG-08 | Upload failure | Clear Retry/Remove; note not falsely Saved with missing image |
| IMG-09 | Undo while upload pending | Paste invalidated; late result ignored |
| IMG-10 | Switch notes while upload pending | No result applied to new note |
| IMG-11 | Close/reopen after completed paste | Correct relative path and visual image |
| IMG-12 | Image between H2 and H3 | Both headings remain headings after reopen |
| IMG-13 | Image after code block | Fence remains balanced; image remains separate |
| IMG-14 | Image after list/task list | List structure and image boundary preserved |
| IMG-15 | Image after table | Table and image both round-trip |
| IMG-16 | Image after callout/raw HTML | Prior block unchanged |
| LOC-01 | Five sentinel blocks before image paste | All semantic fingerprints unchanged |
| LOC-02 | Five sentinel blocks after image paste | All semantic fingerprints unchanged |
| LOC-03 | Paste in middle of long mixed note | No out-of-scope node changes |
| CODE-01 | Multiline paste inside active code block | All lines remain inside same block |
| CODE-02 | Paste HTML/XML inside code block | Literal tags preserved |
| CODE-03 | Paste text with tabs | Tabs preserved |
| CODE-04 | Leading/trailing blank lines | Preserved exactly |
| CODE-05 | Whitespace-only paste | Whitespace inserted, not discarded |
| CODE-06 | Paste backticks/fences | Code text preserved; outer fence remains valid |
| CODE-07 | Paste Windows CRLF | Only line endings normalize to LF |
| CODE-08 | Toolbar Paste code | Same conversion as keyboard code paste |
| CODE-09 | Chrome StartFragment wrapper | Wrapper removed, selected text preserved |
| CODE-10 | Copy/cut code in Chromium | Plain text only; exact selection |
| CODE-11 | Copy/cut code in Firefox | Plain text only; exact selection |
| CODE-12 | Copy/cut code in WebKit | Plain text only; exact selection |
| SAVE-01 | Open and close without edit | No PUT, rewrite, modified change, or history |
| SAVE-02 | Type during slow save | Newer edit stays dirty and is saved next |
| SAVE-03 | Two saves respond out of order | Older response cannot mark newer revision Saved |
| SAVE-04 | Metadata update during save | No document replacement |
| SAVE-05 | True external disk edit | Conflict is surfaced |
| SAVE-06 | Same local revision with stale ETag | Adopt/retry safely without hot loop |
| SAVE-07 | Ctrl+S during normal edit | Latest validated revision saves immediately |
| SAVE-08 | Ctrl+S during pending paste | No incomplete Markdown save; clear status |
| SAVE-09 | Page hide with valid dirty revision | Best-effort save uses latest validated revision |
| SAVE-10 | Page hide with placeholder pending | Existing file not corrupted; pending state handled explicitly |
| RT-01 | Every existing round-trip fixture | Supported semantic content preserved |
| RT-02 | Tiptap doc -> Markdown -> doc | Semantic comparator passes |
| RT-03 | Markdown -> doc -> Markdown without edit | No user edit emitted |
| RT-04 | Unsupported raw content | Preserved or Source-forced, never dropped |
| RT-05 | Transient URL injected in test doc | Validator blocks save |
| RT-06 | Fused image+heading injected | Validator blocks save and reports code |
| RT-07 | Unbalanced code fence injected | Validator blocks save |
| UI-01 | Heading fold state through metadata update | Fold state not reset by reparse |
| UI-02 | Scroll and selection through metadata update | Both remain stable |
| UI-03 | Pop-out and main window editing different notes | Session/result isolation |
| UI-04 | Mobile image paste | Placeholder and resolution usable at narrow viewport |
| UI-05 | Mobile code paste | Content remains in code block; toolbar remains usable |

For async and race tests, run each critical scenario repeatedly with deterministic artificial delays. A single passing run is not enough.

Minimum repetition gate:

- H-08, IMG-02, IMG-03, IMG-07, LOC-01, CODE-01, SAVE-02, and SAVE-03: 50 deterministic iterations each in CI or a dedicated stress test.

---

## 9. Regression matrix for existing features

The coding agent must not declare completion based only on the reported failures. Verify these existing behaviors:

### Markdown and formatting

- YAML front matter remains outside visual editing and preserves unknown keys.
- Links with spaces and special characters remain correct.
- Wiki links continue to suggest and serialize as currently designed.
- Underline, color, font size, highlight, and strike preserve their supported HTML/Markdown representation.
- Hard breaks and paragraph breaks retain current intended behavior.
- Raw HTML safety detection still forces Source mode when appropriate.

### Lists, tasks, and tables

- Nested lists round-trip.
- Task checked state round-trips.
- Empty task items are either preserved correctly or explicitly protected from an unsupported visual save.
- Table header and body rows round-trip.
- Multiline table cells do not silently lose lines. If the Markdown codec cannot represent them, preserve as HTML/raw content rather than degrading them.

### Code

- Language selector remains functional.
- Inline code editing remains basic Tiptap editing, consistent with the prior rollback.
- CodeMirror Edit dialog still synchronizes to one code-block node.
- Diagnostics and PSScriptAnalyzer integration remain unchanged.
- Snippet insert/save behavior remains available.
- Copy button and plain-text selection copy remain available.

### Images and attachments

- Existing relative images display.
- Existing remote images display.
- Broken image UI and Remove action remain.
- Non-image attachment paste/drop remains a normal link.
- Preserve-page HTML sidecars still work.
- Rename/move continues to rewrite asset stems through the backend.
- Static HTML export renders images and code correctly.

### Navigation and editor chrome

- Outline finds headings produced by the new command.
- Heading jumps work.
- Fold controls work.
- Toolbar pin/autohide behavior remains.
- Find-in-note remains.
- Mobile toolbar remains usable.
- Pop-out editor remains isolated.

### Recovery

- History snapshots are still generated before a real content-changing save.
- Restore remains undoable through a pre-restore snapshot.
- Conflict compare/reload/overwrite/keep-editing actions remain.
- Trash and duplicate behavior remain.

---

## 10. Files expected to change or be added

The coding agent may adjust this layout, but must keep responsibilities separated.

### Likely modified files

- `src/Web/src/NoteEditor.tsx`
- `src/Web/src/headingSelection.ts`
- `src/Web/src/pasteHtml.ts`
- `src/Web/src/pasteCodeBlock.ts`
- `src/Web/src/copyCodePlain.ts`
- `src/Web/src/ImageView.tsx`
- `src/Web/src/App.tsx`
- `src/Web/src/frontMatter.ts`
- `src/Web/package.json`
- `docs/changelog.md`
- `CHECKLIST.md`
- `STATUS.md`
- `THIRD_PARTY_NOTICES.md` when dependencies change
- `src/Web/src/jotdexAiPrompt.ts` if editor semantics or AI-facing formatting guidance changes

### Suggested new files

- `src/Web/src/editor/markdown/EditorMarkdownCodec.ts`
- `src/Web/src/editor/markdown/semanticCompare.ts`
- `src/Web/src/editor/markdown/saveSafetyValidator.ts`
- `src/Web/src/editor/extensions/BlockImageMarkdown.ts`
- `src/Web/src/editor/assets/AttachmentResolver.ts`
- `src/Web/src/editor/paste/PasteSessionManager.ts`
- `src/Web/src/editor/paste/PendingAssetPlaceholder.ts`
- `src/Web/src/editor/revisions/EditorRevisionCoordinator.ts`
- `src/Web/src/editor/revisions/SaveCoordinator.ts`
- `src/Web/src/editor/operations/operationMeta.ts`
- `src/Web/src/editor/testing/createTestEditor.ts`
- `src/Web/src/editor/**/*.test.ts`
- `tests/E2E/editor-reliability.spec.ts`
- `tests/RoundTripFixtures/editor-boundary-torture/`
- `docs/decisions/ADR-editor-round-trip-and-paste-transactions.md`

### Backend changes

Avoid backend changes unless a clear contract requires them. The current backend already provides atomic writes, content-hash ETags, history, and attachment storage. A backend change may be appropriate for:

- A typed upload response that clearly separates attachment metadata from document replacement
- Shared document-equivalence test vectors
- Optional repair scanner endpoints

Do not move editor-specific normalization into the server without proving it preserves external Markdown files.

---

## 11. Test tooling and commands

Add stable scripts so a coding agent and CI can run the same gates.

Suggested web scripts:

```json
{
  "scripts": {
    "test": "vitest run",
    "test:watch": "vitest",
    "test:editor": "vitest run src/editor",
    "test:e2e": "playwright test",
    "test:e2e:editor": "playwright test tests/E2E/editor-reliability.spec.ts"
  }
}
```

Required final gate commands, adjusted to the repository's actual working directories:

```text
npm run lint
npm run test
npm run build
dotnet test Jotdex.sln
npm run test:e2e
scripts/probe.ps1
scripts/probe-auth.ps1
scripts/probe-write.ps1
scripts/probe-todos.ps1
```

Where Windows-only probes cannot run in CI, document the manual packaged-build matrix and preserve an automated equivalent where possible.

---

## 12. Diagnostics and observability

Add structured editor diagnostics behind development logging or an explicit local diagnostic toggle.

Allowed fields:

- Timestamp
- Note ID
- Note session ID
- Editor revision
- Save revision
- Operation ID and kind
- Paste session ID
- Node type counts
- Selection and placeholder positions
- Upload count/status
- Serializer duration
- Validator diagnostic codes
- Whether `setContent()` ran and its reason
- Save request/response generation

Forbidden fields:

- Note text
- Clipboard text or HTML
- Code text
- Attachment bytes
- Image data URLs
- Passwords, tokens, cookies
- Full local paths when existing redaction rules omit them

Add a development assertion that every `setContent()` call supplies a permitted reason from the Reload invariant. This will make accidental metadata reloads visible immediately.

---

## 13. Performance contract

Correctness takes priority, but the reliability work should also remove unnecessary full-document work.

### Required measurements

Create representative fixtures for:

- Small note: approximately 5 KB
- Medium technical note: approximately 100 KB with code, tables, and images
- Large note: at least 1 MB or the largest safe representative note from the test vault

Measure:

- Parse time
- Serialize time
- Keystroke transaction time
- Rich-paste insertion time excluding network
- Full paste completion time with fake uploads
- Number of full serializations per typing burst
- Number of `setContent()` calls per note session
- Number of saves/history snapshots per logical operation

### Acceptance

- Metadata-only updates cause zero full document serializations and zero `setContent()` calls.
- Selection-only transactions cause zero Markdown serializations.
- A normal typing burst should produce one debounced serialization, not one per keystroke.
- A completed multi-image paste should produce one final validated serialization under normal conditions.
- No representative benchmark may regress by more than 20% without a documented, reviewed reason.
- The large-note editor must remain interactive while uploads are pending.

Do not add aggressive normalization or background workers that make correctness harder to reason about merely to hit an arbitrary benchmark.

---

## 14. Rollout and rollback

### 14.1 Feature flag

Use a local feature flag such as `editorReliabilityV2` during development and canary testing if the refactor cannot be merged in safely isolated slices.

The flag must not create two different vault formats. Both paths must read and write the same documented Markdown-plus-assets format.

### 14.2 Shadow validation

Before switching fully, the new codec may run in shadow mode:

- Serialize and validate in memory.
- Compare against the current output.
- Record only diagnostic codes and fingerprints.
- Never dual-write files.
- Never upload note content.

### 14.3 Canary

Test against a copied representative vault, not the only live vault.

Canary checklist:

- Open representative notes without edits and verify no file changes.
- Perform all critical heading/image/code cases.
- Verify history and conflict behavior.
- Export static HTML and inspect images/code.
- Run integrity scan.
- Create a backup/move kit before using the build against the primary vault.

### 14.4 Rollback

Rollback must be possible by restoring the previous executable or disabling the feature flag. No canonical file migration is allowed as part of this editor reliability release.

---

## 15. Release documentation

The changelog entry must explain:

- Block images now serialize with safe block boundaries, preventing headings after images from reopening as literal Markdown.
- Partial heading conversion now uses deterministic block replacement.
- Image and rich paste now use stable paste sessions and preserve the original insertion location.
- Attachment metadata no longer reparses the note.
- Code paste paths are consolidated and preserve exact code text.
- Autosave now acknowledges revisioned, validated Markdown rather than intermediate editor states.
- Existing already-corrupted notes are not silently rewritten; use the optional repair scanner when available.

Update STATUS and CHECKLIST with stable work-package IDs. Add an ADR describing the codec and transactional paste boundary.

---

## 16. Explicit non-goals for the first reliability release

Do not do any of the following in the same initial release:

- Replace Tiptap with another editor
- Convert the vault to a new file format
- Automatically rewrite every Markdown file
- Automatically repair ambiguous existing notes
- Remove Source mode
- Remove any paste mode
- Remove HTML sidecars
- Remove callouts, tables, tasks, wiki links, folds, colors, font size, snippets, or the CodeMirror Edit dialog
- Redesign the entire application shell
- Add cloud telemetry
- Switch to the official Tiptap Markdown extension without the separate parity gate
- Treat a regex postprocessor as the sole Markdown correctness layer

---

## 17. Definition of done

The editor reliability project is complete only when all of these statements are true:

1. The exact image-followed-by-H3 defect is reproduced in a pre-fix test and passes after the fix.
2. The exact active-code multiline paste defect is covered even though 1.1.23 already contains a targeted fix.
3. Partial heading conversion uses deterministic block replacement.
4. Attachment metadata changes do not call `setContent()`.
5. Async image completion resolves a unique placeholder at the original paste position.
6. No `paste.invalid`, `blob:`, normal `data:image`, or `/api/attachments/` URL can pass the save validator.
7. Supported editor documents pass semantic serialize/parse round-trip checks.
8. A malformed serializer result is blocked before PUT and does not overwrite disk.
9. Five semantic sentinel blocks above and below image paste remain unchanged.
10. One logical multi-image paste normally creates no more than one content save and one history snapshot.
11. Code paste preserves leading/trailing whitespace and whitespace-only payloads.
12. Open-without-edit creates no PUT, rewrite, modified timestamp change, or history snapshot.
13. Slow and out-of-order save responses cannot clear or replace newer edits.
14. True external changes still produce conflict UX.
15. All existing editor features in the regression matrix remain functional.
16. Frontend unit/integration tests, Playwright tests, production build, .NET tests, and applicable probes pass.
17. The representative canary vault passes integrity scan and static export inspection.
18. Documentation, ADR, changelog, STATUS, CHECKLIST, and dependency notices are updated.
19. The release is rollback-safe and does not require a vault migration.
20. No claim of a fixed user report is made without a deterministic automated reproduction or clearly labeled evidence.

---

## 18. Coding-agent operating instructions

Use this section as the direct behavioral contract for the implementation agent.

1. Read `AGENTS.md`, `STATUS.md`, `CHECKLIST.md`, `docs/changelog.md`, `docs/vault-format.md`, ADR 0003, ADR 0004, the code-editor ADR, and the product brief before editing.
2. Verify the current repository head. This contract was written against `9ef298b8e230ad97c98584019d569706646bdb8a`; adapt to later changes rather than overwriting them.
3. Start with failing tests. Do not begin by rewriting `NoteEditor.tsx` wholesale.
4. Preserve version 1.1.23's active-code-block `insertText` fix.
5. Fix the exact block-image serializer boundary first and prove it with round-trip tests.
6. Replace partial heading insertion with a whole-block replacement transaction.
7. Remove attachment inventory from document reload triggers before refactoring async paste.
8. Introduce stable paste placeholders and note-session checks. Never resolve by a global URL search.
9. Separate dirty events from validated Markdown events.
10. Do not save a document containing transient nodes or URLs.
11. Do not silently retry a true conflict as last-write-wins.
12. Keep changes reviewable. Prefer the work-package/PR sequence in Section 7.
13. Run relevant tests after every work package and the full gate before release.
14. Append durable rationale to `docs/changelog.md`; do not rewrite history.
15. Update the AI prompt only when editor semantics exposed to AI have changed.
16. Never log user content while diagnosing editor transactions.
17. Do not claim the whole product is fixed because one manual paste succeeded. Run the stress repetitions.
18. Leave the vault format unchanged and keep rollback simple.

---

## 19. Recommended PR sequence

### PR 1 — Test harness and block-boundary protection

- Vitest/Playwright setup
- Exact failing image/heading test
- Jotdex block image serializer
- Save-safety boundary checks
- No major paste refactor yet

### PR 2 — Deterministic heading transformations

- Rewrite `headingSelection.ts`
- Selection/Undo/Redo/round-trip tests
- Nested-context rules

### PR 3 — Attachment resolver and no metadata reload

- Canonical asset source in editor state
- NodeView display resolver
- Typed metadata events
- Remove attachment-triggered `setContent()`

### PR 4 — Transactional image/rich paste

- Paste session manager
- Stable placeholders
- Async cancellation and failure UI
- One final validated commit

### PR 5 — Code clipboard consolidation

- Shared code paste command
- Exact whitespace behavior
- Browser clipboard tests

### PR 6 — Revision-aware autosave

- Dirty/validated split
- Revision coordinator
- Save coordinator
- Shared sameness vectors
- Race/conflict tests

### PR 7 — Hardening, canary, documentation, and release

- Full matrix
- Stress tests
- Benchmarks
- ADR/changelog/status/checklist
- Portable build and rollback verification

### Separate research PR — Official Markdown extension parity

- No production switch
- Corpus comparison and ADR only unless all gates pass

### Separate optional PR — Existing-note repair scanner

- Report and preview first
- Explicit user approval required

---

## 20. Final implementation mandate

Build this as a reliability boundary, not a list of patches.

The lasting design should make these statements true:

- Jotdex owns what its Markdown means.
- A visual editor operation has a stable identity and one safe committed result.
- Attachment metadata cannot rewrite note content.
- Async upload timing cannot change insertion location or neighboring blocks.
- A heading is transformed as a block, not inserted into inline space.
- Code paste is plain, exact, and consistent everywhere.
- Autosave saves only validated revisions and understands which revision a response acknowledges.
- The editor refuses to overwrite a good file when round-trip safety cannot be proven.
- Tests exercise the same parser, serializer, commands, clipboard events, and browser paths used by the product.

That is the standard required for the Jotdex editor to feel dependable without sacrificing the features already built.
