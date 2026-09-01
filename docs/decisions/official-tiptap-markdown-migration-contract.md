# Jotdex Official Tiptap Markdown Migration Contract

**Target:** Replace the unmaintained community `tiptap-markdown` bridge with the maintained official `@tiptap/markdown` package while preserving Jotdex's existing editor behavior, vault portability, autosave safety, paste reliability, history, search, exports, snippets, task metadata, and password protection.

**Repository:** `jcline123/jotdex`

**Current baseline:** Jotdex 1.1.24 or later, including the central `EditorMarkdownCodec`, transactional image-paste sessions, attachment resolver, revision-aware autosave, save-safety validation, Vitest coverage, and Playwright scaffolding.

**Recommended target release:** 1.2.0. A prerelease or internal migration build may be used before 1.2.0, but the final 1.2.0 runtime must not contain or depend on `tiptap-markdown`.

**Contract status:** Implementation directive. This document is not a suggestion list. The coding agent must meet every MUST requirement before declaring the migration complete. A requirement may be changed only when the replacement is documented in an ADR and is at least as safe for existing vault files.

---

## 1. Executive decision

Jotdex will remain a Tiptap editor. This project is not a rewrite of the editor UI and is not a switch to a different rich-text framework. It is a controlled replacement of the Markdown parsing and serialization engine:

- **Legacy engine:** community package `tiptap-markdown@0.9.0`.
- **Official engine:** `@tiptap/markdown`, pinned to the exact same Tiptap version as the rest of Jotdex during the migration.

The official package is maintained by Tiptap and exposes the right long-term extension points: `contentType: 'markdown'`, `editor.getMarkdown()`, `editor.markdown.parse()`, `editor.markdown.serialize()`, `parseMarkdown`, `renderMarkdown`, and `markdownTokenizer`.

The official package is still documented as Beta and currently has known edge cases. Therefore, merely changing the import and calling `editor.getMarkdown()` is explicitly forbidden. Jotdex must own and test a **Jotdex Markdown dialect adapter** around the official package.

The end state must satisfy all of the following:

1. `tiptap-markdown` is absent from `package.json`, `package-lock.json`, the production bundle, development dependencies, and transitive dependency output.
2. The official package is the only Markdown engine used at runtime.
3. Every persistent Jotdex node and mark has an explicit official Markdown representation or is deliberately forced to Source mode.
4. Existing vault files remain normal UTF-8 Markdown plus sibling `.assets` folders.
5. Opening and closing a note without editing it does not rewrite the note.
6. No comment, task metadata, callout, styled span, code character, image path, table value, heading, link, front-matter field, or unsupported raw block is silently dropped.
7. The actual password-protected application is exercised through authenticated browser tests. Authentication must not be disabled or bypassed to make the tests pass.
8. Any raw-vault cleanup is performed through an auditable staging migration with backups, hashes, per-file decisions, and rollback.

---

## 2. Why this is a migration rather than a dependency update

The legacy and official packages differ in several important ways:

- Legacy output is read from `editor.storage.markdown.getMarkdown()`.
- Official output is read from `editor.getMarkdown()` or `editor.markdown.serialize(json)`.
- Official Markdown input must be explicitly identified with `contentType: 'markdown'`.
- Official parsing uses MarkedJS rather than the legacy bridge's markdown-it/prosemirror-markdown path.
- Official serialization depends on `renderMarkdown` handlers registered by each extension.
- A persistent custom node without a registered renderer can serialize to an empty string.
- HTML comments are not natively guaranteed to survive.
- Markdown table cells are limited to one child block, and current official versions have had a control-character serialization defect for multi-block cells.
- CommonMark soft line breaks currently require a Jotdex guard because affected official versions can expose literal newline characters in editor text nodes.

Jotdex has durable content that predates this engine. The migration must therefore prove compatibility against the real Jotdex dialect, not only generic Markdown examples.

---

## 3. Current-state baseline that must be preserved

The coding agent must start by reading and understanding at least these current files before changing dependencies:

- `AGENTS.md`
- `STATUS.md`
- `CHECKLIST.md`
- `docs/changelog.md`
- `docs/vault-format.md`
- `docs/decisions/0003-note-history-and-autosave.md`
- `docs/decisions/0004-markdown-plus-assets.md`
- `src/Web/package.json`
- `src/Web/src/NoteEditor.tsx`
- `src/Web/src/editor/markdown/EditorMarkdownCodec.ts`
- `src/Web/src/editor/markdown/saveSafetyValidator.ts`
- `src/Web/src/editor/markdown/semanticCompare.ts`
- `src/Web/src/editor/extensions/createEditorExtensions.ts`
- `src/Web/src/editor/extensions/BlockImageMarkdown.ts`
- `src/Web/src/editor/extensions/PendingAssetPlaceholder.ts`
- `src/Web/src/editor/paste/PasteSessionManager.ts`
- `src/Web/src/editor/revisions/EditorRevisionCoordinator.ts`
- `src/Web/src/editor/revisions/SaveCoordinator.ts`, if present
- `src/Web/src/frontMatter.ts`
- `src/Web/src/unsafeMarkdown.ts`
- `src/Web/src/callout.ts`
- `src/Web/src/todosMarkdown.ts`
- `src/Infrastructure/Vault/VaultTaskService.cs`
- `src/Infrastructure/Vault/NoteCommandService.cs`
- `tests/RoundTripFixtures/**`
- `src/Web/src/editor/markdown/editorReliability.test.ts`
- `src/Web/e2e/editor-reliability.spec.ts`
- `src/Server/Auth/AuthEndpointExtensions.cs`

The 1.1.24 reliability architecture is a prerequisite, not temporary code to discard. In particular, the migration MUST preserve:

- `EditorMarkdownCodec` as the only production parse/serialize boundary.
- Transactional image placeholders and paste sessions.
- Attachment inventory updates that do not call `setContent()`.
- Revision-aware autosave.
- Save-safety validation.
- Deterministic partial-heading conversion.
- Exact code-box paste behavior.
- The rule that pending uploads cannot be serialized or saved.
- Existing conflict, history, Source-mode, and rollback behavior.

Do not move Markdown parsing back into `NoteEditor.tsx`. Do not duplicate parsing logic across UI components.

---

## 4. Non-negotiable product invariants

### 4.1 Files remain the product

Markdown files and `.assets` folders remain canonical. SQLite, Tiptap JSON, migration manifests, test snapshots, and browser state are all rebuildable or auxiliary.

No feature may require a proprietary JSON document to preserve content.

### 4.2 No silent loss

If the official engine cannot safely represent a note or a fragment, Jotdex must do one of the following:

1. Preserve the fragment through a tested custom node or mark.
2. Preserve it as a raw block with an explicit round-trip representation.
3. Open the note in Source mode with a specific reason.

It must never parse the note visually and then emit a version with the unsupported content missing.

### 4.3 No automatic live-vault rewrite

Installing the new release, opening Jotdex, browsing folders, opening a note, toggling rails, searching, or viewing a note must not bulk-normalize the vault.

A note may be reserialized only after a real user edit or an explicit migration operation. The migration operation must use the staging and backup rules in this contract.

### 4.4 No authentication backdoor

Do not add a hidden test password, query-string bypass, development header bypass, unauthenticated migration endpoint, hard-coded cookie, or environment switch that disables authentication on a configured installation.

Automated tests must authenticate normally or run against an isolated installation whose password is created by the test harness.

### 4.5 One engine owns a document at a time

Never register both Markdown extensions in one Tiptap editor. Never parse with one engine and silently save with the other in the same note session.

### 4.6 Exact dependency alignment

All `@tiptap/*` packages used by Jotdex must resolve to one exact version during this migration. Do not combine the Markdown-engine switch with a general Tiptap upgrade.

At the current baseline, that means pinning all Tiptap packages to `3.29.2` and adding `@tiptap/markdown` at `3.29.2`. If the branch starts from a later exact baseline, use that exact version consistently and record it in the ADR.

### 4.7 Rollback remains real

The previous portable release and the pre-migration vault copy must be sufficient to return to the old application and old files. A rollback procedure that depends on the new executable is not sufficient.

---

## 5. Terminology

- **Source body:** Markdown after YAML front matter has been separated, before visual parsing.
- **Canonical output:** Markdown Jotdex intentionally emits after a real edit or explicit migration.
- **Semantic parity:** Same headings, block order, text, marks, code, links, tasks, images, table values, callout type/content, and supported style attributes, even if harmless whitespace differs.
- **Byte parity:** Exact same bytes, including line endings and spacing.
- **Persistent extension:** Any node or mark that can exist in a saved editor document.
- **Transient extension:** An editor-only node that must never be saved, such as a pending upload placeholder.
- **Source-only note:** A note the visual editor cannot safely represent. The raw file remains editable in Source mode.
- **Shadow comparison:** Running both codecs against the same source or document for diagnostics while only one engine controls the live editor.
- **Migration manifest:** Machine-readable record of every input file, hash, output file, hash, classification, warning, and action.

---

## 6. Definition of success

The migration is complete only when all of these statements are true:

- The official codec parses every supported fixture and every approved real-vault note without content loss.
- Unsupported notes reliably open Source-only rather than being partially represented.
- The official codec passes the full editor reliability suite in Chromium, Firefox, and WebKit.
- An authenticated Playwright session opens, edits, saves, reloads, and validates a note through the actual API and disk file.
- Anonymous API and browser access remain blocked when a password is configured.
- The staged real vault passes link, asset, front matter, task, code-fence, control-character, and semantic checks.
- A before/after report identifies every file that would change and why.
- The user can restore the original vault and previous executable using tested steps.
- `npm ls tiptap-markdown` shows no installed package in the final branch.
- `npm ls @tiptap/markdown` shows the expected exact version.
- `npm run build`, frontend tests, .NET tests, and E2E tests are green.
- The portable release starts without Node.js installed.

---

## 7. Target architecture

### 7.1 Codec interface

Retain `EditorMarkdownCodec`, but make the implementation explicit:

```ts
export type MarkdownEngineId = 'legacy' | 'official'

export interface EditorMarkdownCodec {
  readonly engine: MarkdownEngineId
  parse(markdownBody: string): ParseResult
  serialize(doc: PmNode): SerializeResult
  compareSemantic(a: PmNode, b: PmNode): SemanticComparison
  inspect(markdownBody: string): MarkdownInspection
  destroy?(): void
}
```

During development, implement:

- `LegacyMarkdownCodec` using the existing community package.
- `OfficialMarkdownCodec` using `@tiptap/markdown`.
- `DifferentialMarkdownCodec` or a separate comparison service used only by tests and migration tooling.

The final production branch must instantiate only `OfficialMarkdownCodec`. The legacy implementation and package must be removed after the comparison artifacts have been captured.

### 7.2 Extension factory

Change the extension factory to accept an engine:

```ts
createEditorExtensions({
  markdownEngine: 'official',
  withReactNodeViews,
  attachments,
  wikiOnChange,
})
```

The factory must append exactly one Markdown extension:

```ts
import { Markdown } from '@tiptap/markdown'

Markdown.configure({
  indentation: {
    style: 'space',
    size: CANONICAL_LIST_INDENT,
  },
  markedOptions: {
    gfm: true,
    breaks: false,
    pedantic: false,
  },
})
```

`CANONICAL_LIST_INDENT` must be selected from evidence: compare the current legacy serializer, fixtures, templates, and real vault. Record the choice in the ADR. Do not choose it only because it is the official default.

### 7.3 Official codec behavior

The official codec must use the official API directly:

```ts
const json = editor.markdown?.parse(markdownBody)
const markdown = editor.markdown?.serialize(doc.toJSON())
```

or the equivalent commands:

```ts
editor.commands.setContent(markdownBody, {
  contentType: 'markdown',
  emitUpdate: false,
})

const markdown = editor.getMarkdown()
```

The implementation must not read `editor.storage.markdown.getMarkdown()`.

### 7.4 Typed content operations

Create a single module, for example:

`src/Web/src/editor/operations/contentInsertion.ts`

It must export explicit operations:

```ts
setMarkdownDocument(editor, markdown, options)
insertMarkdown(editor, markdown)
insertHtml(editor, html)
insertLiteralText(editor, text)
replaceWithJson(editor, json)
```

Required behavior:

- `setMarkdownDocument` always passes `contentType: 'markdown'`.
- `insertMarkdown` always passes `contentType: 'markdown'`.
- `insertHtml` always passes `contentType: 'html'` or inserts validated JSON created from HTML.
- `insertLiteralText` uses a ProseMirror text transaction and never treats text as HTML or Markdown.
- `replaceWithJson` passes JSON and does not set Markdown content type.

Audit every direct `setContent`, `insertContent`, and `insertContentAt` call in the frontend. Classify each string as Markdown, HTML, or literal text, and replace it with the corresponding helper.

At minimum, this audit must cover:

- Initial note content.
- External note reloads.
- Source-to-Visual conversion.
- Rich paste.
- Plain paste.
- Attachment-link insertion.
- Wikilink insertion.
- Template insertion.
- Clip-page insertion.
- Snippet insertion.
- Code-box insertion.
- Test-editor creation.

Add a source-scanning test that fails when editor-related code introduces a direct raw string call to `setContent`, `insertContent`, or `insertContentAt` outside the approved helper module.

### 7.5 Persistent schema coverage gate

Add a test that enumerates the effective Tiptap schema and Markdown handler registry.

For every persistent node and mark that can appear in a document, the test must prove one of:

- The official engine has a registered parser and renderer.
- A Jotdex override supplies the parser and renderer.
- The feature is read-only and causes Source-only mode.

The only allowed persistent-serialization exception is an explicitly whitelisted transient node such as `pendingAsset` that is already blocked by the save-safety validator.

The test must fail if a persistent node can serialize to an empty string.

### 7.6 Parser and serializer preprocessing

The official codec may use a narrowly scoped Jotdex preprocessor or postprocessor for documented official defects and Jotdex syntax. These transforms must:

- Be deterministic.
- Be fixture-tested.
- Skip fenced code and inline code when handling comment-like or Markdown-like text.
- Produce diagnostics when they change source representation.
- Never use broad regular expressions that can alter literal code or escaped syntax.
- Preserve a semantic fingerprint before and after.

Every transform must be named and individually testable. A generic `fixMarkdown()` function is forbidden.

---

## 8. Dependency and version policy

### 8.1 Migration branch dependency state

The migration branch may temporarily contain both packages for differential tests:

```json
{
  "dependencies": {
    "@tiptap/markdown": "3.29.2",
    "tiptap-markdown": "0.9.0"
  }
}
```

All Tiptap packages must be changed from caret ranges to exact versions for the duration of the migration.

### 8.2 Final dependency state

Before the final release:

- Remove `tiptap-markdown` from `dependencies` and `devDependencies`.
- Remove imports, type shims, storage access, and legacy-only helpers.
- Regenerate `package-lock.json` from a clean install.
- Run `npm ci` in a clean directory.
- Confirm the production bundle contains no legacy package strings or modules.
- Update `THIRD_PARTY_NOTICES.md` to add the official package and remove the community package when no longer present.

### 8.3 Future upgrades

After migration, Tiptap upgrades must be treated as content-format changes:

1. Pin the proposed exact version on a branch.
2. Run the complete dialect, fixture, real-vault audit, and browser suite.
3. Review official Markdown changelog and open `area: markdown` issues.
4. Do not merge an automatic dependency bump that changes any Tiptap package without these checks.

---

## 9. Required implementation sequence

The coding agent must follow the gates in order. Do not remove the legacy package before the official engine can be compared against it.

### MDM-00 - Freeze and recovery baseline

1. Record current branch, commit, package lock hash, executable version, and vault marker version.
2. Create a Git tag or clearly named baseline branch before migration work.
3. Build and retain the current portable ZIP.
4. Create a Jotdex Move Kit.
5. Create a raw filesystem copy of the vault, including hidden files and all `.assets` directories.
6. Create a SHA-256 manifest for every file in that copy.
7. Verify that at least one note, one image, one snippet, and `Todos.md` can be restored from the backup.
8. Never use the only live vault copy for development or E2E mutation.

**Gate:** Recovery artifacts exist, hashes are recorded, and restore has been spot-tested.

### MDM-01 - Official-engine spike in isolation

1. Add the official package at the exact current Tiptap version.
2. Create a minimal headless official codec outside the live editor.
3. Parse and serialize a small representative set:
   - Heading.
   - Paragraph with bold and italic.
   - Code block.
   - Task list.
   - Table.
   - Image followed by H3.
   - Styled span.
   - Jotdex task comment.
   - Callout.
4. Document every unsupported or changed feature before integrating it.

**Gate:** A written compatibility table exists. No production path uses the official engine yet.

### MDM-02 - Dual codec and differential harness

1. Extract the existing community implementation into `LegacyMarkdownCodec` without changing behavior.
2. Add `OfficialMarkdownCodec` behind the same interface.
3. Parameterize extension creation by engine.
4. Parameterize the existing editor reliability tests so the legacy tests remain a baseline and the official tests expose gaps.
5. Add differential reports for parse JSON, semantic fingerprint, and serialized Markdown.

**Gate:** Both engines can run in the same test process using separate editor instances. They are never registered together.

### MDM-03 - Jotdex dialect extensions

Implement the official Markdown support described in Sections 10 through 19 of this contract. Do not use post-serialization regex patches in place of extension handlers unless the transform is specifically approved and syntax-aware.

**Gate:** Every persistent schema type is covered or forces Source-only mode.

### MDM-04 - Typed command migration

1. Add typed insertion helpers.
2. Replace all ambiguous raw string insertion calls.
3. Add the source audit test.
4. Set `contentType: 'markdown'` on initial content and all Markdown reloads.
5. Ensure HTML paste remains HTML and plain paste remains literal text.

**Gate:** No ambiguous content command remains in editor code.

### MDM-05 - Automated fixture and browser parity

1. Run all existing editor reliability tests against the official engine.
2. Add the migration-specific test matrix in Section 25.
3. Add authenticated real-app Playwright tests.
4. Fix failures without weakening validators or deleting fixtures.

**Gate:** Unit, integration, and all three browser projects are green.

### MDM-06 - Raw vault audit and staged cleanup

1. Run the migration auditor against a copied real vault.
2. Review every unsafe or source-only note.
3. Produce a staged output vault and report.
4. Apply only deterministic approved changes.
5. Re-run audit against staged output until there are no unexplained changes.

**Gate:** The staged vault has a complete manifest, no unresolved critical diagnostics, and no missing assets or links.

### MDM-07 - Official engine as default with temporary rollback switch

1. Use the official engine in a prerelease or internal build.
2. Keep a hidden process-level legacy switch only in the migration build, not a per-user formatting option.
3. Exercise the staged vault for ordinary editing, paste, search, snippets, exports, and restart.
4. Do not silently fall back to legacy on a per-note parse error. A parse error must open Source mode and report the reason.

**Gate:** Official default has passed the staged-vault soak and authenticated E2E suite.

### MDM-08 - Remove legacy package and release

1. Delete the legacy codec and feature flag.
2. Remove the package and regenerate the lock file.
3. Repeat clean install, build, unit, .NET, E2E, portable publish, and startup checks.
4. Publish the migration report and rollback instructions with the release artifacts.

**Gate:** Final branch and portable artifact have no community package and all acceptance criteria pass.


---

## 10. Jotdex Markdown dialect

The official package is the engine, but Jotdex owns the durable dialect. The coding agent must document the dialect in a new ADR and in `docs/vault-format.md`.

The supported dialect consists of:

- CommonMark paragraphs, headings, blockquotes, horizontal rules, links, emphasis, strong emphasis, strikethrough, inline code, and fenced code.
- GitHub-flavored tables and task lists.
- Relative image and attachment paths into the note's sibling `.assets` directory.
- Standard relative Markdown links for resolved internal note links.
- Preserved unresolved wikilinks where they already exist.
- Obsidian-style callout syntax for Jotdex callouts.
- Limited safe inline HTML spans for text color and font size.
- Jotdex task/todo metadata comments attached to checklist lines.
- Sanitized, explicitly supported raw HTML only where Jotdex already promises visual editing.
- Source-only preservation for unsupported raw HTML and unknown directives.

The dialect must not depend on implementation-specific attachment API URLs, browser blob URLs, data URLs, pending-upload markers, or Tiptap JSON.

---

## 11. Image contract

### 11.1 Existing behavior to preserve

Images are block nodes in Jotdex. They are stored as normal files and referenced with relative Markdown paths. An image must remain at its insertion point through asynchronous upload. Attachment metadata must not reload the editor document.

### 11.2 Official parser problem to handle explicitly

Markdown image tokens are normally inline tokens, while Jotdex configures the Image extension as a block node. The official parser must not produce an invalid paragraph containing a block image.

Implement a Jotdex block-image parser that recognizes a paragraph whose meaningful content is exactly one Markdown image token and returns a top-level `image` node.

The parser must distinguish:

- A standalone image paragraph: supported as a block image.
- Whitespace around a standalone image: supported and normalized.
- An image followed by a heading: supported as two blocks.
- Multiple standalone images: supported as separate image blocks.
- An image mixed inline with prose: not silently rearranged. Either preserve through a tested inline-image representation or force Source mode with a clear reason.

### 11.3 Official renderer

Replace the legacy `addStorage().markdown.serialize` implementation with an official `renderMarkdown` handler.

The renderer must:

- Escape alt text correctly.
- Preserve the title when present.
- Encode or escape destinations without changing an already-valid relative asset path.
- Never emit `/api/attachments/...`, `blob:`, `data:`, or `paste.invalid` URLs.
- Guarantee a valid block boundary before the next heading, paragraph, list, code block, callout, table, rule, or image.
- Avoid accumulating additional blank lines over repeated parse/serialize cycles.

Do not assume the same newline behavior as the legacy serializer. Prove the exact official parent/child joining behavior with tests before deciding whether the renderer itself returns trailing newlines.

### 11.4 Optional dimensions

Audit whether any real note or editor path persists image width or height.

- If dimensions are not persisted today, keep Markdown image output unchanged.
- If dimensions are persisted, plain Markdown cannot carry them. Serialize that image as a sanitized `<img>` element with only `src`, `alt`, `title`, `width`, and `height` attributes, and prove it parses back to the same image node.
- Never drop a non-null dimension silently.

### 11.5 Image acceptance cases

At minimum:

- Local image then H1 through H6.
- Remote image then H3.
- H3 then image.
- Image between two paragraphs.
- Image between a list and a heading.
- Image path containing spaces, parentheses, apostrophes, brackets, percent signs, Unicode, `#`, `?`, and `&` after sanitization/encoding.
- Alt text containing brackets and backslashes.
- Title containing quotes.
- Two equal image URLs resolve to the correct unique placeholders.
- Uploads complete in reverse order.
- Cursor moves while upload is pending.
- Note changes while upload is pending.
- Retry and Remove on failure.
- One hundred parse/serialize cycles without fused blocks or accumulating whitespace.

---

## 12. Code block contract

Code is a first-class Jotdex feature. The engine migration must not alter code characters to improve generic Markdown appearance.

### 12.1 Required preservation

Preserve:

- Tabs.
- Spaces and indentation.
- Backslashes.
- PowerShell backticks.
- Quotes.
- Angle brackets.
- Ampersands.
- Leading blank lines inside the block.
- Intentional trailing blank lines represented by the node.
- Unknown language identifiers.
- Empty code blocks.
- Fences pasted as content.

### 12.2 Fence selection

The serializer must select a fence long enough that literal backtick or tilde runs inside the code cannot close it. If the official inherited CodeBlock renderer does not satisfy this, create `JotdexCodeBlockMarkdown` by extending `CodeBlockLowlight` and override official `parseMarkdown`/`renderMarkdown`.

The output must contain exactly the structural newlines needed for the fence. Repeated cycles must not add an extra blank line to the code body.

### 12.3 Paste behavior

Keep the 1.1.24 exact-text code paste path. The official Markdown engine must not be involved when characters are inserted into an existing code block.

All of these routes must use the shared exact-text code command:

- Regular paste while selection is in a code block.
- Paste mode: Code.
- Toolbar Paste code.
- Snippet insertion.
- CodeMirror Edit dialog save.

### 12.4 Code safety gates

The save validator must continue to reject unbalanced output fences. Add detection for a serializer that accidentally emits code content as live HTML or normal Markdown blocks.

---

## 13. Task-list and metadata-comment contract

### 13.1 Durable syntax

Jotdex relies on comments such as:

```markdown
- [ ] Review backup <!-- jotdex-task id="..." priority="high" due="..." remind="..." -->
```

and in `Todos.md`:

```markdown
- [ ] Call dentist <!-- jotdex-todo id="..." priority="normal" remind="off" -->
```

These comments are application data stored in the canonical Markdown file. They are not disposable formatting comments.

### 13.2 Required custom extension

Implement a persistent inline atom, suggested name `jotdexTaskMetadata`, with:

- `kind`: `task` or `todo`.
- `raw`: original full comment text.
- Parsed known attributes: `id`, `priority`, `due`, `remind`.
- Preserved unknown attributes.
- Stable attribute order when the comment is not edited.

Add a high-priority official inline `markdownTokenizer` that recognizes only Jotdex task/todo comments. It must run before generic HTML comment handling and must not match inside fenced code or inline code.

Add:

- `parseMarkdown` to create the inline metadata node.
- `renderMarkdown` to emit the comment on the same task line.
- A noneditable node view that does not interfere with typing, checkbox clicks, selection, Backspace, or arrow navigation.

When task attributes are updated through the Todos rail, the backend may emit canonical attribute order. A visual editor round trip without a task edit must preserve the comment semantically and must not remove unknown attributes.

### 13.3 List interaction

The official package has had task-list and mixed-list defects in earlier releases. Add explicit tests for:

- Paragraph followed by task list.
- Horizontal rule followed by task list.
- Code block followed by task list.
- Bullet list adjacent to task list.
- Ordered list adjacent to task list.
- Nested task list.
- Empty task text.
- Task metadata after bold, link, inline code, and escaped characters.
- Completed task with metadata.
- Stable task ID after unrelated edits above the task.
- Task rail update and completion after official editor save.

### 13.4 Backend parity

Run integration tests against `VaultTaskService` after official saves. The backend must still discover, update, and complete the same task IDs.

The migration is not complete if the editor looks correct but the Todos rail loses priority, due date, reminder, or task identity.

---

## 14. Generic HTML comment contract

The official package documents comments as unsupported. Jotdex must not rely on the default comment behavior.

### 14.1 Required policy

- Jotdex task/todo comments are fully supported through the typed metadata node in Section 13.
- Unknown generic comments must either round-trip through a dedicated raw-comment node or force Source-only mode.
- Comments must never be silently dropped.
- A comment in a heading must not crash the editor or produce a block node inside heading content.

### 14.2 Preferred implementation

Implement separate inline and block raw-comment nodes with custom tokenizers:

- `rawHtmlCommentInline`
- `rawHtmlCommentBlock`

Each node stores the exact raw comment and renders it unchanged.

For inline comments, use an inline atom that is valid in paragraphs, headings, list items, and table cells. For block comments, use a block atom.

The visual representation may be a subtle nonprinting marker or a small `HTML comment` chip. It must be clear enough that a user does not unknowingly delete hidden content while selecting adjacent text.

### 14.3 Safe fallback

If a comment context cannot be represented without affecting structure, `OfficialMarkdownCodec.inspect()` must mark the note Source-only before visual parsing.

### 14.4 Tests

Include:

- Comment between paragraphs.
- Comment at start/end of note.
- Comment inside heading.
- Comment after task item.
- Comment inside blockquote.
- Comment containing `>` and quotes.
- Comment-like text inside inline code and fenced code, which must remain code and must not become comment nodes.
- Multiple adjacent comments.

---

## 15. Callout contract

### 15.1 Canonical syntax

Use portable Obsidian-style blockquote callouts as the canonical Markdown representation:

```markdown
> [!NOTE]
> Callout content
```

Supported types:

- `NOTE`
- `TIP`
- `INFO`
- `WARNING`
- `DANGER`

### 15.2 Backward-compatible parsing

The official engine must parse both:

1. The canonical `> [!TYPE]` form.
2. Existing `<blockquote data-callout="type">...</blockquote>` HTML created or preserved by earlier Jotdex versions.

The migration may normalize legacy HTML callouts to canonical callout Markdown in the staged vault after semantic parity is proven.

### 15.3 Custom tokenizer

Add a block tokenizer for the callout form. It must:

- Recognize the marker only at the beginning of a blockquote callout.
- Preserve nested paragraphs, lists, code blocks, links, and blank lines within the callout.
- Reject unknown callout types without dropping the blockquote. Unknown types should remain a normal blockquote or cause a diagnostic.
- Avoid matching callout-looking text inside code fences.

Use `parseBlockChildren` where blank lines in callout content must survive.

### 15.4 Renderer

The renderer must prefix every body line correctly with `>` and preserve blank blockquote lines as `>`.

Do not infer or delete the first callout paragraph. Current node content must remain content. Separating a callout title into a new data model is a different project.

### 15.5 Tests

- Every supported type.
- Empty callout.
- Multi-paragraph callout.
- List and task list inside callout.
- Code fence inside callout.
- Nested blockquote.
- Legacy HTML callout.
- Callout followed immediately by heading, image, table, or code.
- One hundred cycles.

---

## 16. Text color and font-size contract

### 16.1 Existing durable representation

Jotdex uses the `textStyle` mark with color and font-size attributes. The legacy package could emit limited HTML spans because it was configured with HTML support.

The official TextStyle and Color extensions do not by themselves guarantee a Markdown renderer for Jotdex's combined attributes. Jotdex must add one.

### 16.2 Jotdex TextStyle extension

Extend `TextStyle` with official Markdown support.

Parsing:

- Reuse safe `<span style="...">` HTML parsing.
- Preserve only supported `color` and `font-size` attributes in visual mode.
- Reject event handlers, classes used for active content, URLs, custom properties, expressions, and unknown style declarations.

Rendering:

```html
<span style="color: #175cd3; font-size: 1.25em">text</span>
```

Requirements:

- Stable property order.
- Safe HTML-attribute escaping.
- No empty `style` span.
- Adjacent identical spans may merge without changing content.
- Nested/overlapping bold, italic, strike, code, link, color, and size must serialize to valid Markdown/HTML and parse back with the same marks.

### 16.3 Allowed values

The current toolbar values are always allowed. Existing vault values may be allowed when they pass a strict CSS-value parser.

At minimum, allow:

- Current Jotdex color palette.
- Safe hex colors.
- Safe `rgb()`/`rgba()` values if already present.
- Current font sizes: `0.85em`, `1em`, `1.25em`, `1.5em`.
- A narrowly reviewed set of `em`, `rem`, `%`, and `px` values if present in imported content.

Unknown style properties or unsafe values must not be dropped in visual mode. Mark the note Source-only or preserve the exact raw span with a tested raw-inline node.

### 16.4 Tests

- Color only.
- Size only.
- Color and size together.
- Styled bold/italic/link text.
- Partially overlapping marks.
- Nested spans from OneNote exports.
- Adjacent spans.
- Unsupported style property.
- Malicious style input.
- Style immediately before/after inline code and image.

---

## 17. Table contract

### 17.1 One-child cell rule

The official engine supports one child block per Markdown table cell. Jotdex must enforce this before serialization.

Create a table compatibility validator that walks every table cell.

Supported cell structure:

- One paragraph containing text, inline marks, links, inline code, and explicit hard-break nodes.

Unsupported without normalization:

- Multiple paragraphs.
- Lists in a cell.
- Code blocks in a cell.
- Block images in a cell.
- Callouts or nested tables in a cell.

### 17.2 Multi-block cell handling

A multi-block cell must never be serialized directly by a known-affected official renderer because it can emit an invisible U+001F unit-separator character.

Choose one of these behaviors per note:

1. Deterministically flatten safe multiple paragraphs into one paragraph separated by hard breaks rendered as `<br>`.
2. Block visual save and require Source mode.

Do not collapse lists, code blocks, or other structural content into plain text without explicit user action.

### 17.3 Control-character validation

Expand save-safety validation to reject all unexpected C0 control characters in Markdown output, especially U+001F. Allow only tab, LF, and CR where appropriate.

### 17.4 Table parity

Test:

- Header and body rows.
- Empty cells.
- Escaped pipes.
- Inline code containing pipes.
- Links in cells.
- Bold/italic in cells.
- Hard break in cell.
- Alignment markers, if Jotdex currently preserves them.
- Pasted Excel/HTML table with and without a valid header row.
- Table followed by heading, image, list, and paragraph.
- Multi-block cell refusal/normalization.
- No control characters in output.

---

## 18. Soft break, hard break, and blank-line contract

### 18.1 Canonical meaning

Jotdex must use CommonMark meaning:

- A single unescaped newline inside a prose paragraph is a soft break and displays as normal flowing text.
- A blank line separates paragraphs.
- Two trailing spaces or a trailing backslash can represent an explicit hard break when parsed from source.
- Shift+Enter in structural contexts may create a hard break as already defined by Jotdex.

### 18.2 Official soft-break guard

Affected official versions can parse CommonMark soft line breaks into literal newline characters that display as visible line breaks.

Implement and test a narrow JSON post-parse normalization that:

- Replaces soft newline characters in ordinary prose text nodes with the appropriate space.
- Does not touch code blocks, inline code, explicit hard-break nodes, raw comments, or syntax where a newline is structurally meaningful.
- Records a cosmetic-normalization diagnostic for migration reporting.

Do not implement this as a global `markdown.replace(/\n/g, ' ')` operation.

### 18.3 Blank paragraphs

Test the official behavior after headings, tables, HTML blocks, callouts, images, and code fences. Multiple visual blank paragraphs may normalize, but the engine must not unexpectedly create or delete meaningful blocks while editing neighboring content.

### 18.4 Tests

- Hard-wrapped paragraph.
- Explicit two-space hard break.
- Backslash hard break.
- Shift+Enter behavior in paragraph, list, task, blockquote, table, and code.
- `<br>` from rich paste.
- Blank lines after heading, table, HTML block, image, code, and callout.
- No extra blank paragraph on each reopen.

---

## 19. Links and wikilinks contract

### 19.1 Standard links

Preserve:

- Relative note links.
- Relative attachment links.
- URL titles.
- Spaces and special characters.
- Fragment identifiers.
- Mailto links if already supported.

Do not double-encode percent escapes. Do not convert a relative vault link into an application URL.

### 19.2 Resolved wikilinks

Jotdex autocomplete currently resolves a selected note into a standard relative Markdown link. Keep this behavior.

The insertion route must call `insertMarkdown()` rather than an ambiguous raw `insertContent(string)`.

### 19.3 Unresolved wikilinks

The vault-format contract says unresolved `[[wikilinks]]` are preserved. The official serializer may escape brackets if they remain plain text.

Implement one of these tested solutions:

- A dedicated `unresolvedWikiLink` inline node with a tokenizer and exact renderer.
- A protected raw-inline representation that preserves the exact `[[target]]` string.

Do not silently turn `[[Note]]` into `\[\[Note\]\]` unless the audit proves all Jotdex consumers and users accept that as canonical. Exact preservation is preferred.

### 19.4 Backlinks and search

After migration, verify that:

- Backlinks still detect standard relative links.
- Unresolved wikilinks remain searchable.
- Moving/renaming a note still rewrites only intended relative links.
- Attachment links remain valid after note rename/move.


---

## 20. Raw HTML and Source-mode contract

### 20.1 Visual-mode admission is explicit

Retain and expand `looksUnsafeForVisual()`. Before loading a note body into the official visual editor, run a capability inspection that identifies:

- Dangerous HTML.
- Unknown block tags.
- Unsupported inline tags or styles.
- Forms, media, iframes, scripts, event handlers, and JavaScript URLs.
- Comments that cannot be represented.
- Inline images mixed with prose if the block-image model cannot represent them.
- Multi-block table cells.
- Unknown custom directives.
- Malformed Markdown structures that the official parser would partially drop.

The result must be one of:

- `visual-safe`
- `visual-safe-with-canonicalization`
- `source-only`
- `invalid`

The UI must display the specific reason for Source-only mode.

### 20.2 Source mode remains a raw editor

Source mode must continue to edit the actual Markdown body rather than official Tiptap JSON.

- Typing in Source mode may autosave raw Markdown through the normal ETag/history path.
- The official codec is required only when entering Visual mode or running an explicit compatibility check.
- A Source-mode save must not silently parse and reserialize the body.
- Toggling Source to Visual must validate first. On failure, remain in Source with diagnostics.

### 20.3 Supported HTML

Visual mode may support only reviewed HTML representations needed by Jotdex, including:

- Safe style spans.
- Legacy callout blockquotes.
- Supported image HTML when dimensions are needed.
- Deliberate raw-comment nodes.

Everything else is either represented by an explicit extension or remains Source-only.

### 20.4 No reliance on silent official fallback

Do not assume the official engine's fallback behavior will preserve unknown HTML. Some paths can turn unsupported content into literal text, empty content, or invalid inline/block combinations. Jotdex must decide support before loading the note visually.

---

## 21. Paste and upload contract

The Markdown-engine migration must not regress the 1.1.24 paste architecture.

### 21.1 Rich paste remains HTML input

Rich clipboard HTML must be cleaned by `cleanPasteHtml()` and inserted as HTML or validated JSON. It must not be passed to the official Markdown parser.

Use:

```ts
insertHtml(editor, cleanedHtml)
```

not:

```ts
editor.commands.insertContent(cleanedHtml)
```

without an explicit content type.

### 21.2 Plain paste remains literal

Plain paste and Shift+paste must use a literal text transaction. Markdown-looking text such as `# heading`, `- list`, `<div>`, or `![image](x)` must remain literal in Plain mode.

### 21.3 Markdown insertion is deliberate

Only routes whose product meaning is Markdown may use `insertMarkdown()`, such as:

- A generated attachment link.
- A resolved internal note link.
- A template body when intentionally inserting a Markdown template.

### 21.4 Pending assets remain nonserializable

The official schema may contain `pendingAsset` during an upload, but:

- It must have no persistent Markdown renderer.
- Schema coverage must whitelist it only as transient.
- `EditorRevisionCoordinator` and `saveSafetyValidator` must prevent save while any pending or failed placeholder remains.
- Replacing a placeholder with an image must not call `setContent()`.

### 21.5 Upload ETags

Attachment-only changes must not be treated as document-body replacements. Preserve the 1.1.24 event split between:

- `attachments-updated`
- `etag-confirmed`
- `replace-document`

Do not reload the Markdown body solely because attachment inventory changed.

### 21.6 Paste E2E requirements

Authenticated browser tests must perform real DOM paste or equivalent browser clipboard events for:

- Plain text.
- Rich HTML.
- A screenshot/file image.
- Rich HTML with a data image.
- Rich HTML with a remote image using a mocked safe import endpoint.
- Multiple images finishing out of order.
- Paste into an existing code block.
- Paste mode: Code.

The test must save, reload from the server, and inspect the actual disk Markdown.

---

## 22. Autosave, history, and concurrency contract

### 22.1 Preserve revision-aware saves

The official engine must plug into the existing `EditorRevisionCoordinator`. Do not return to serializing the whole document on every selection or formatting transaction.

A saveable revision is created only when:

1. The document changed.
2. No paste session is pending.
3. The official codec serialized successfully.
4. Save-safety validation passed.
5. Semantic validation passed.

### 22.2 Initial load is not an edit

When the official engine parses a note, differences in its in-memory representation must not mark the note dirty.

The original raw Markdown body remains the baseline until a user transaction occurs. Opening, scrolling, changing selection, folding headings, updating attachment inventory, or switching browser tabs must not schedule a PUT.

### 22.3 First official save safety

The first user edit of a note under the official engine may canonicalize unrelated formatting in that note. Before allowing the first official save in the released migration:

- The whole vault must have passed the audit.
- The normal history service must snapshot the exact pre-save Markdown.
- The full pre-migration vault backup must still exist.
- The UI must not claim `Saved` until the server confirms the exact revision.

### 22.4 ETag behavior

Keep real external conflicts visible. Do not hide a semantic conflict by repeatedly adopting the disk ETag and applying the latest editor body as last-write-wins.

Retry is allowed only for a proven stale response to an earlier local revision when:

- The note ID and note session match.
- The current disk document is semantically equal to the acknowledged local baseline.
- The retry count is bounded.

### 22.5 History

History snapshots must contain the exact prior Markdown file. A restore made after migration must be readable by both the current official release and the retained previous portable release when possible.

Add a regression test:

1. Open legacy-format note with official engine.
2. Edit and save.
3. Confirm pre-save history snapshot equals original bytes except unavoidable server timestamp behavior.
4. Restore.
5. Confirm original content returns.

---

## 23. Search, outline, backlinks, todos, templates, and export parity

The editor migration is incomplete if note editing works but adjacent product features interpret the resulting Markdown differently.

### 23.1 Search

Verify that the search extractor still indexes:

- Headings.
- Prose.
- Code block text.
- Link text and destinations where intended.
- Callout text.
- Task text without exposing metadata as noisy content.
- Attachment names.

Run representative exact searches for commands, IP addresses, registry paths, error codes, and code snippets before and after migration.

### 23.2 Outline and heading folds

Official serialization must preserve heading levels and inline marks. Outline extraction must return the same visible heading text. Heading fold state is UI-only and must not alter Markdown.

### 23.3 Backlinks

Compare backlinks before and after staged migration. No valid backlink may disappear because of changed URL escaping or link destination formatting.

### 23.4 Todos

Run both standalone `Todos.md` and from-note task workflows:

- List.
- Edit priority.
- Edit due date.
- Edit reminder.
- Complete.
- Undo where supported.
- Reload after save.

### 23.5 Templates

Run every template through the official codec. Generated templates must not immediately canonicalize into unsafe or materially different structure.

Update template Markdown only when the migration report documents why.

### 23.6 Snippets

Every snippet note must preserve:

- Front matter.
- Language.
- Trigger.
- Exact fenced code body.
- Leading/trailing code whitespace.

Test opening a snippet in the main editor, editing through CodeMirror, inserting it into another code box, saving, and reopening.

### 23.7 Share HTML and static export

The server renders Markdown with Markdig. For each fixture and staged-vault sample:

- Render before and after canonicalization.
- Compare normalized text, headings, lists, code, links, images, tables, and callouts.
- Ensure code remains escaped in `<pre><code>`.
- Ensure local images resolve.
- Ensure no pending or application-only URLs appear in exported HTML.

If canonical callout syntax requires a Markdig extension or preprocessor for matching output, implement and test it in both Share HTML and static export.

---

## 24. Vault audit and migration tool

### 24.1 Purpose

Create an offline developer/admin tool that uses the same production official codec and extension set to inventory, compare, stage, and optionally apply canonical Markdown changes.

Suggested location:

`tools/MarkdownEngineMigration/`

The tool may require Node.js on the development/migration machine. The final Jotdex portable runtime must not require Node.js.

### 24.2 Required commands

The tool must support noninteractive commands similar to:

```powershell
npm run markdown:migrate -- audit --vault "C:\JotdexVault" --output "C:\JotdexMigration\audit"

npm run markdown:migrate -- stage --vault "C:\JotdexVault" --output "C:\JotdexMigration\staged"

npm run markdown:migrate -- apply --vault "C:\JotdexVault" --staged "C:\JotdexMigration\staged" --manifest "...\manifest.json"

npm run markdown:migrate -- verify --vault "C:\JotdexMigration\staged" --manifest "...\manifest.json"

npm run markdown:migrate -- rollback --vault "C:\JotdexVault" --backup "C:\JotdexMigration\backup"
```

Exact command syntax may differ, but all capabilities are required.

### 24.3 Default safety

- Default command is read-only audit.
- `stage` writes to a different destination and never alters source.
- `apply` requires an explicit flag and a manifest whose source hashes still match every live input file.
- If any source hash changed after staging, abort the entire apply before writing anything.
- Never follow a junction or symlink outside the vault.
- Never include application secrets or history in a vault-only migration.
- Preserve `.assets` bytes exactly unless an explicit image-extraction repair is approved.

### 24.4 Environment matching

The audit must run in a DOM-capable environment matching production parsing, using jsdom or a real browser as required. Do not accept a headless result when the official parser has browser-only HTML behavior.

At least one verification pass must run the same corpus in a real Chromium browser.

### 24.5 File inventory

Inventory every `.md` file, including:

- Root notes.
- Nested notes.
- `Todos.md` and orphaned `Todos (n).md` files.
- Reserved `Snippets/` notes.
- Notes with missing or malformed front matter.
- Hidden/imported notes that the UI may not list.

Inventory each note's sibling `.assets` directory and every local Markdown reference.

### 24.6 Per-file analysis

For every note, record:

- Relative path.
- File size.
- SHA-256.
- BOM presence.
- Line-ending style.
- Final newline presence.
- Front matter presence and parse status.
- Unknown front-matter keys.
- Body feature inventory.
- Legacy parse success.
- Official parse success.
- Legacy semantic fingerprint.
- Official semantic fingerprint.
- Official serialized hash.
- Change classification.
- Diagnostics.
- Source-only reason, if any.
- Local links and whether targets exist.
- Image/attachment references and whether files exist.
- Remote image count.
- Task metadata count and duplicate IDs.
- Callout count and types.
- Raw HTML/comment count.
- Table-cell compatibility.
- Code-fence balance.
- Unexpected control characters.

### 24.7 Differential passes

During the temporary dual-engine stage, calculate:

1. `legacy.parse(source)`
2. `official.parse(source)`
3. `legacy.serialize(legacyDoc)`
4. `official.serialize(officialDoc)`
5. `official.serialize(legacyDoc)` where schema-compatible
6. `legacy.serialize(officialDoc)` where schema-compatible
7. Semantic fingerprints for each valid document
8. A normalized block-by-block diff

Do not classify a note as safe only because normalized text matches. Compare structural features and attributes.

### 24.8 Classifications

Every note receives exactly one primary classification:

- `exact`: Official round trip is byte-identical.
- `cosmetic`: Only approved whitespace, marker, or line-wrap changes.
- `canonical`: Intentional Jotdex dialect normalization with no semantic change.
- `feature-change`: Representation changed and requires review.
- `source-only`: Preserved but not safe for visual editing.
- `unsafe`: Content, structure, metadata, or references would be lost or corrupted.
- `invalid-source`: Original file is malformed and requires manual repair.

### 24.9 Reports

Produce:

- `manifest.json`: complete machine-readable data.
- `summary.html`: counts, filters, before/after examples, and links to per-note reports.
- `changes.csv`: one row per note.
- `unsafe.txt`: relative paths and reasons only.
- `hashes-before.sha256` and `hashes-after.sha256`.
- `migration.log`: operation metadata without note bodies or secrets.

Do not commit reports containing personal vault content to Git.

### 24.10 Staged vault

The staged destination must contain a complete usable vault, not only changed files.

- Copy unchanged files byte-for-byte.
- Write changed files atomically.
- Copy assets byte-for-byte unless an approved repair requires creating a new asset.
- Preserve creation/modified front-matter values during cosmetic migration.
- Preserve file timestamps when content is unchanged.
- Do not make every note rise to the top of the notes list merely because of engine migration.

### 24.11 Verification

After staging:

1. Re-run the audit against staged output.
2. Start an isolated Jotdex server on the staged vault.
3. Rebuild the index from scratch.
4. Run authenticated browser smoke and mutation tests.
5. Run integrity scan.
6. Run Share HTML for representative notes.
7. Run a static export.
8. Stop and restart the server.
9. Confirm no note changed merely by being opened.


---

## 25. Raw-vault cleanup policy

The user authorizes cleanup when it is needed for the official engine, but that authorization does not permit speculative rewriting. Cleanup is governed by the following rules.

### 25.1 General rule

A cleanup may be applied automatically only when all of these are true:

1. The original pattern is unambiguously identified.
2. The intended result can be derived without guessing.
3. Semantic fingerprint and feature inventory are equal after repair.
4. All local references still resolve.
5. The repair has a dedicated regression fixture.
6. The original file is retained in the migration backup.
7. The manifest records the exact reason and transform ID.

### 25.2 Approved deterministic cleanup categories

The migration tool MAY automatically stage these repairs after tests exist:

#### CLEAN-01 - Fused block boundary

Repair a legacy serializer defect such as:

```markdown
![image](path)### Heading
```

to:

```markdown
![image](path)

### Heading
```

Only when the left side parses as a complete image destination and the right side is a valid ATX heading marker at a block boundary.

#### CLEAN-02 - Resolvable attachment API URL

Replace `/api/attachments/{id}` in a Markdown image/link destination with the canonical relative `.assets` path only when the attachment ID resolves to the current note's known attachment and the target file exists.

#### CLEAN-03 - Known temporary image marker

Remove or repair `paste.invalid`, `blob:`, pending-upload markers, or other application-temporary image sources only when the correct local attachment can be established through upload metadata, file hash, or a unique matching asset.

If the correct asset cannot be proven, classify as unsafe. Do not delete the reference.

#### CLEAN-04 - Valid embedded data image extraction

A `data:image/...;base64,...` image may be extracted to the note's `.assets` folder when:

- The data decodes successfully.
- MIME type and magic bytes agree.
- Size is within limits.
- A collision-safe filename is generated.
- The Markdown reference is replaced with a relative path.
- The created asset hash is recorded.

#### CLEAN-05 - Legacy callout normalization

Convert tested `<blockquote data-callout>` blocks to canonical `> [!TYPE]` syntax when child structure and callout type round-trip exactly.

#### CLEAN-06 - Supported style-span canonicalization

Normalize a span containing only supported color/font-size declarations into stable property order and safe syntax.

Do not strip unknown style declarations. Such a note remains Source-only or requires manual review.

#### CLEAN-07 - Soft-wrap canonicalization

Reflow CommonMark soft-wrapped prose into a normal flowing paragraph only when explicit hard-break markers are absent and the semantic text is unchanged.

This is cosmetic and should be applied only to files already changing for another approved reason unless the user explicitly requests whole-vault reflow.

#### CLEAN-08 - Line-ending normalization on changed files

When an approved cleanup changes a file, write UTF-8 without BOM and LF line endings unless the audit identifies a file whose encoding/BOM is intentionally preserved by existing vault rules.

Do not rewrite an otherwise unchanged file solely to normalize line endings.

#### CLEAN-09 - Task metadata canonicalization

Canonicalize attribute ordering or quoting only when all known and unknown attributes are preserved and the task ID is unchanged.

#### CLEAN-10 - URL destination encoding

Normalize a local asset or note-link destination only when decoded target resolution is the same before and after. Do not double-encode existing percent escapes.

### 25.3 Manual-review or Source-only categories

Do not automatically repair:

- Ambiguous unmatched code fences.
- Literal prose that happens to begin with heading or list characters.
- Inline images mixed with text when block conversion would change order.
- Unknown raw HTML layouts.
- Scripts, iframes, forms, media, SVG with active content, or custom elements.
- Multi-block table cells containing lists, code, images, or other blocks.
- Broken local links with more than one possible target.
- Duplicate task IDs where the correct identity cannot be inferred.
- Malformed YAML front matter.
- Unknown Markdown directives.
- Overlapping style markup whose intended marks cannot be proven.
- A data image that fails MIME, size, or decode validation.
- A missing asset when no matching file exists.
- Any transform whose semantic comparison fails.

### 25.4 Applying the staged vault

Preferred cutover:

1. Stop Jotdex.
2. Confirm no process is writing the live vault.
3. Rename the original vault to a dated backup location on the same volume when possible.
4. Move the fully verified staged vault into the configured path.
5. Preserve the original backup as read-only.
6. Start Jotdex.
7. Rescan/reindex.
8. Run authenticated smoke tests.

Do not perform hundreds of normal note PUT requests for a bulk migration because that would bump note `modified` values, generate noisy history, reorder note lists, and create avoidable concurrency risk.

### 25.5 Rollback from cleanup

Rollback must be possible by stopping Jotdex and restoring the original directory. Verify the original SHA-256 manifest before reopening it.

---

## 26. Password-protected automated testing contract

Jotdex's password protection is part of the product and must be exercised, not worked around by weakening it.

### 26.1 Two test modes

Implement two explicit Playwright modes:

#### Ephemeral protected mode - required for CI and ordinary agent work

- Create a temporary vault copied from `tools/SampleVault` plus migration fixtures.
- Create a temporary Jotdex data root.
- Start Jotdex on an isolated loopback port.
- Confirm `/api/auth/status` reports no password initially.
- Generate a strong random test password in memory.
- Call `/api/auth/setup` to create the test admin and obtain the normal session cookie.
- Confirm `/api/auth/status` now reports password required and authenticated.
- Save Playwright storage state to a gitignored temporary path.
- Run all mutating tests against this isolated protected instance.

This mode gives the coding agent complete automation without knowing or disabling the real installation password.

#### Existing protected instance mode - optional release smoke

Use only against a designated copy/staging vault unless the user explicitly authorizes mutation.

Configuration must be supplied through local secrets outside Git:

- `JOTDEX_E2E_BASE`
- `JOTDEX_E2E_USERNAME`
- `JOTDEX_E2E_PASSWORD_FILE` preferred, or `JOTDEX_E2E_PASSWORD`
- `JOTDEX_E2E_TOTP_SECRET_FILE` or `JOTDEX_E2E_RECOVERY_CODE_FILE` when needed
- `JOTDEX_E2E_ALLOW_MUTATION=1` only for a disposable/staged vault

The harness must never print secret values, include them in traces, attach them to reports, or commit storage-state files.

### 26.2 Playwright authentication setup

Add a Playwright setup project or global setup that:

1. Requests `/api/auth/status`.
2. Uses `/api/auth/setup` only in explicitly isolated mode when no password exists.
3. Otherwise sends exactly one valid `/api/auth/login` request.
4. Handles `requiresTotp` deliberately.
5. Writes authenticated storage state under a gitignored directory such as `src/Web/playwright/.auth/`.
6. Makes Chromium, Firefox, and WebKit projects depend on the authenticated setup.

Do not make repeated password guesses. If credentials are absent or rejected, fail fast with a message naming the missing environment variable, not the value.

### 26.3 TOTP

Most editor migration tests should use the ephemeral protected instance with TOTP off. Add a separate auth test for TOTP.

For an existing TOTP-protected instance:

- Prefer a recovery code supplied through a protected file, or
- Generate an RFC 6238 code from a secret supplied through a protected file using Node's built-in crypto library.

Never log the TOTP secret or generated code.

### 26.4 Anonymous and authenticated gates

Required auth tests:

- Anonymous `/api/notes` request returns 401 when a password is set.
- Anonymous attachment request is blocked according to current auth policy.
- Normal login returns the session cookie.
- Authenticated note read succeeds.
- Authenticated note save succeeds.
- Logout invalidates access.
- Invalid password does not reveal sensitive details.
- Editor tests do not accidentally rely on Development bypass after a password exists.

### 26.5 UI login test

At least one browser test must start without storage state, use the actual login UI, and reach Home/Notes. This catches API-only setup drift.

### 26.6 Server orchestration

Add a script, for example `scripts/run-editor-e2e.ps1`, that:

- Creates temporary vault/data directories.
- Selects free loopback ports.
- Builds the SPA if required.
- Starts the ASP.NET server.
- Polls `/api/health`.
- Runs Playwright.
- Stops the server even on failure.
- Deletes temporary secrets and auth state.
- Retains traces/logs only when a test fails, with redaction.

Do not require the user to manually start the server for the normal suite.

### 26.7 Live-vault mutation protection

The harness must refuse mutating tests when:

- The base URL is not loopback and `JOTDEX_E2E_ALLOW_MUTATION` is absent.
- The vault path matches the configured production vault and no explicit staging marker exists.
- The test note/folder does not use a clearly reserved E2E prefix.

---

## 27. Differential fixture suite

### 27.1 Existing fixtures

Run all existing `tests/RoundTripFixtures` through:

- Legacy parse/serialize.
- Official parse/serialize.
- Official repeated cycles.
- Server Markdig render.
- Search extraction where relevant.

### 27.2 New migration fixture directories

Add fixtures similar to:

```text
tests/MarkdownMigration/
  01-soft-breaks/
  02-comments/
  03-task-metadata/
  04-callouts/
  05-styled-spans/
  06-block-images/
  07-inline-image-source-only/
  08-table-edge-cases/
  09-code-fence-edge-cases/
  10-wikilinks/
  11-one-note-html/
  12-bom-crlf/
  13-invalid-source/
  14-known-official-regressions/
```

Each fixture should contain:

- `input.md`
- `expected.official.md` when safe/canonical
- `expected.semantic.json`
- `expected.diagnostics.json`
- Optional asset files
- A short `README.md` explaining the case

### 27.3 Personal content prohibition

Do not copy real personal or work note text into the repository. Convert real failures into minimal synthetic fixtures with the same structure.

---

## 28. Known official-engine regression guards

The official package is maintained but still evolving. Add permanent tests for at least these categories:

### OFF-01 - Soft line breaks

A hard-wrapped CommonMark paragraph must display as flowing prose, not visible line breaks.

### OFF-02 - Table control character

No U+001F or other unexpected C0 control character may enter output.

### OFF-03 - Comment inside heading

A heading containing an HTML comment must not crash or create invalid heading content. It must preserve the comment or force Source mode.

### OFF-04 - Heading after ordered list

Inline marks in a heading after an ordered list must remain marks rather than literal `**` characters.

### OFF-05 - Task-list adjacency

A task list after a paragraph, rule, or code block must not duplicate characters or create a phantom paragraph.

### OFF-06 - Hard breaks

Two-space and backslash hard breaks must create a hard-break node and serialize correctly.

### OFF-07 - Blank lines after blocks

Parsing must not progressively remove or add blank blocks after headings, tables, HTML blocks, images, code, or callouts.

### OFF-08 - Overlapping marks

Overlapping bold, italic, strike, link, and styled spans must produce valid parseable output.

### OFF-09 - Block image parsing

A standalone Markdown image must become a valid block image, not an invalid child of a paragraph.

### OFF-10 - Unknown persistent node

A synthetic persistent node without a renderer must fail schema coverage and save validation rather than disappear.

These tests remain after the legacy package is removed.

---

## 29. Detailed automated test matrix

The coding agent must implement or preserve tests for the following IDs. Equivalent grouping is acceptable, but each behavior needs a clearly traceable assertion.

### LOAD and no-rewrite

- `LOAD-01` Open supported note, wait past autosave debounce, no PUT.
- `LOAD-02` Open Source-only note, no PUT.
- `LOAD-03` Open note with attachments, attachment inventory update does not call `setContent()`.
- `LOAD-04` Open and close note, disk SHA unchanged.
- `LOAD-05` Switch between notes without edits, both SHAs unchanged.
- `LOAD-06` Reload browser without edits, no history snapshot.
- `LOAD-07` Toggle Source/Visual without edits when exact/canonical-safe, no save.
- `LOAD-08` Failed Visual conversion leaves raw source untouched.

### Front matter

- `FM-01` Front matter remains outside the visual codec.
- `FM-02` Unknown keys preserved exactly.
- `FM-03` Multiline YAML values preserved.
- `FM-04` `created` preserved.
- `FM-05` Migration does not bump `modified` for cosmetic change.
- `FM-06` UTF-8 BOM detected and handled according to policy.
- `FM-07` Malformed front matter becomes manual/source-only, not guessed.

### Headings and paragraphs

- `HEAD-01` H1-H6 parse and serialize.
- `HEAD-02` Inline bold/italic/code/link in heading.
- `HEAD-03` Partial selection creates paragraph/heading/paragraph.
- `HEAD-04` Full-line selection changes one block.
- `HEAD-05` Triple-click overhang does not affect next block.
- `HEAD-06` Heading after ordered list retains marks.
- `HEAD-07` Heading after image remains formatted.
- `HEAD-08` Heading containing protected comment remains valid.
- `HEAD-09` Undo/redo restores exact structure.
- `HEAD-10` Outline text and levels remain stable.

### Images and assets

- `IMG-01` Standalone local image parses as top-level image.
- `IMG-02` Image then H3 uses separate blocks.
- `IMG-03` H3 then image uses separate blocks.
- `IMG-04` Two images remain distinct.
- `IMG-05` Special-character path round trips.
- `IMG-06` Attachment API URL is rejected on save.
- `IMG-07` Pending placeholder blocks save.
- `IMG-08` Out-of-order upload resolution keeps order.
- `IMG-09` Cursor movement does not move uploaded image.
- `IMG-10` Note switch aborts old session updates.
- `IMG-11` Failure retains Retry/Remove placeholder.
- `IMG-12` Asset inventory update does not reload document.
- `IMG-13` Remote image is localized or remains explicitly remote.
- `IMG-14` Inline mixed image triggers tested behavior/source-only.
- `IMG-15` Dimensions preserved if present.

### Code

- `CODE-01` Multiline paste stays in one code block.
- `CODE-02` HTML/XML remains literal.
- `CODE-03` Tabs preserved.
- `CODE-04` Leading blank lines preserved.
- `CODE-05` Whitespace-only content preserved.
- `CODE-06` Backtick fence content cannot close outer fence.
- `CODE-07` CRLF normalizes only as intended.
- `CODE-08` Unknown language preserved.
- `CODE-09` Empty code block survives.
- `CODE-10` One hundred cycles do not add blank lines.
- `CODE-11` CodeMirror edit syncs one block.
- `CODE-12` Snippet insert preserves exact characters.

### Lists and tasks

- `LIST-01` Bullet list.
- `LIST-02` Ordered list start number.
- `LIST-03` Nested mixed lists.
- `LIST-04` Escaped list-looking prose remains prose.
- `TASK-01` Open task with metadata.
- `TASK-02` Completed task with metadata.
- `TASK-03` Nested task.
- `TASK-04` Task after paragraph/rule/code.
- `TASK-05` Bullet list adjacent to task list.
- `TASK-06` Metadata unknown attrs preserved.
- `TASK-07` Todos rail update retains ID.
- `TASK-08` Due/remind/priority survive editor save.
- `TASK-09` Task-like comment in code is untouched.
- `TASK-10` Duplicate IDs reported by auditor.

### Callouts

- `CALL-01` All five types.
- `CALL-02` Legacy HTML parse.
- `CALL-03` Multi-paragraph content.
- `CALL-04` Nested list and code.
- `CALL-05` Blank lines.
- `CALL-06` Block after callout remains separate.
- `CALL-07` Unknown type safe fallback.
- `CALL-08` One hundred cycles.

### Styles and marks

- `STYLE-01` Color.
- `STYLE-02` Font size.
- `STYLE-03` Combined color/size.
- `STYLE-04` Overlap with bold/italic/link/strike.
- `STYLE-05` Nested imported spans.
- `STYLE-06` Empty span removed without text loss.
- `STYLE-07` Unknown safe-looking style is not silently dropped.
- `STYLE-08` Malicious style rejected/source-only.
- `MARK-01` Bold/italic overlap valid.
- `MARK-02` Leading/trailing whitespace outside delimiters.
- `MARK-03` Literal Markdown punctuation escaped correctly.

### Tables

- `TABLE-01` Basic GFM table.
- `TABLE-02` Empty cells.
- `TABLE-03` Escaped pipe and inline-code pipe.
- `TABLE-04` Marks/links in cells.
- `TABLE-05` Hard break uses supported representation.
- `TABLE-06` Multi-block cell blocked or safely flattened.
- `TABLE-07` No U+001F.
- `TABLE-08` Pasted table header normalization.
- `TABLE-09` Table followed by heading/image/list.
- `TABLE-10` Alignment parity if supported.

### Comments and HTML

- `HTML-01` Jotdex task comment typed node.
- `HTML-02` Generic block comment.
- `HTML-03` Generic inline comment.
- `HTML-04` Heading comment.
- `HTML-05` Comment syntax in code remains code.
- `HTML-06` Supported span.
- `HTML-07` Supported legacy callout HTML.
- `HTML-08` Script/event handler forces Source-only.
- `HTML-09` Unknown complex layout forces Source-only.
- `HTML-10` Source mode saves raw HTML without codec rewrite.

### Links and wikilinks

- `LINK-01` Relative note link.
- `LINK-02` Relative attachment link.
- `LINK-03` URL with query/fragment.
- `LINK-04` Spaces and percent escapes.
- `LINK-05` Title quotes.
- `WIKI-01` Resolved autocomplete inserts relative Markdown.
- `WIKI-02` Unresolved wikilink preserves exact syntax.
- `WIKI-03` Backlinks unchanged after migration.
- `WIKI-04` Rename/move rewriting remains targeted.

### Soft/hard breaks

- `BREAK-01` CommonMark soft wrap displays flowing.
- `BREAK-02` Two-space hard break.
- `BREAK-03` Backslash hard break.
- `BREAK-04` Shift+Enter paragraph behavior.
- `BREAK-05` Shift+Enter list/task/table/blockquote behavior.
- `BREAK-06` Smart paste `<br>` behavior.
- `BREAK-07` Blank-line stability after all block types.

### Save and concurrency

- `SAVE-01` Exact revision confirmed before Saved.
- `SAVE-02` Older response cannot clear newer dirty state.
- `SAVE-03` Attachment event cannot replace body.
- `SAVE-04` Real disk conflict remains visible.
- `SAVE-05` Same-document ETag conflict adopts safely.
- `SAVE-06` Pending paste suppresses save.
- `SAVE-07` Validation failure blocks PUT.
- `SAVE-08` Unload flush uses validated latest revision.
- `SAVE-09` First official save creates recoverable history.
- `SAVE-10` Restore returns original content.

### Authentication and real app

- `AUTH-01` Test setup creates a normal password-protected instance.
- `AUTH-02` Anonymous note API returns 401.
- `AUTH-03` Login API creates valid cookie.
- `AUTH-04` UI login reaches app.
- `AUTH-05` Authenticated create/edit/save/reload succeeds.
- `AUTH-06` Logout blocks access.
- `AUTH-07` Development bypass is not used after password setup.
- `AUTH-08` Secret values absent from logs/traces.
- `AUTH-09` Existing-instance read-only mode refuses mutation by default.

### Vault migration

- `MIG-01` Audit is read-only.
- `MIG-02` Stage leaves source hashes unchanged.
- `MIG-03` Apply aborts on changed source hash.
- `MIG-04` Assets copy byte-for-byte.
- `MIG-05` Front matter and timestamps follow policy.
- `MIG-06` Every changed note has transform IDs/reasons.
- `MIG-07` Unsafe note is not rewritten.
- `MIG-08` Rollback restores before hashes.
- `MIG-09` Staged vault re-audit has no critical unexplained differences.
- `MIG-10` Previous portable release opens original backup.

### Export/search/product integration

- `EXP-01` Share HTML text/structure parity.
- `EXP-02` Code escaped.
- `EXP-03` Images embedded/resolved.
- `EXP-04` Callouts render acceptably.
- `EXP-05` Static export completes.
- `SEARCH-01` Code/IP/error exact searches unchanged.
- `SEARCH-02` Callout and task text indexed.
- `SEARCH-03` No task metadata noise.
- `PRODUCT-01` Templates safe.
- `PRODUCT-02` Snippets exact.
- `PRODUCT-03` Todos exact.
- `PRODUCT-04` Pop-out editor uses official engine and flushes safely.

---

## 30. Repeated-cycle and fuzz testing

### 30.1 Repeated cycles

For every safe migration fixture, run at least 100 official parse/serialize cycles and assert:

- Semantic fingerprint remains constant.
- Block count/type sequence remains constant.
- No control characters appear.
- No temporary URLs appear.
- No comments or task attributes disappear.
- Blank lines do not grow or shrink after the first approved canonicalization.
- Output reaches a fixed point.

### 30.2 Property/fuzz cases

Generate bounded combinations of:

- Paragraphs, headings, lists, tasks, images, rules, code, blockquotes, tables, and callouts.
- Bold, italic, strike, inline code, links, color, and size.
- Unicode, emoji, punctuation, backslashes, brackets, and whitespace.

For valid generated documents:

```text
JSON -> official Markdown -> official JSON
```

must retain the semantic fingerprint.

Fuzzing must use fixed seeds and save the minimal failing seed as a permanent fixture.

---

## 31. Performance and memory requirements

The official engine must not make ordinary editing materially slower.

Measure at least:

- Parse time for 10 KB, 100 KB, and representative largest real note.
- Serialize time for the same notes.
- Typing latency with autosave debounce.
- Rich paste with 1, 5, and 20 images.
- Memory after opening/switching 100 notes.
- Headless codec editor lifetime.

Requirements:

- Do not instantiate a new full editor for every keystroke.
- A shared headless codec instance must be isolated from the live editor and reset safely between operations.
- Test-created editors must be destroyed.
- Migration tooling may be slower, but must stream files and bound memory rather than loading the entire vault and all assets at once.
- Large-note serialization must occur only at revision flush/save boundaries, not on selection changes.

Record baseline and final measurements in the migration report.

---

## 32. Observability and diagnostics

Add local-only diagnostics sufficient to troubleshoot without logging note bodies.

Useful fields:

- Engine ID and exact package version.
- Note ID, not note text.
- Operation ID/revision.
- Parse/serialize duration.
- Input/output byte count.
- Semantic fingerprint hashes.
- Diagnostic codes.
- Source-only reason.
- SetContent reason.
- Paste session ID and counts.

Never log:

- Note body.
- Clipboard contents.
- Password.
- TOTP secret/code.
- Session cookie.
- Attachment contents.

Add a Settings/Maintenance diagnostic showing the active Markdown engine and version, without exposing a production switch after legacy removal.

---

## 33. Rollout and release plan

### 33.1 Migration development build

Both packages may exist. Official engine is used only in tests, audit tooling, or an explicitly selected local development build.

### 33.2 Official-default release candidate

- Official engine controls the editor.
- A process-level legacy switch may remain only for rollback during the candidate period.
- The switch must require restart and be clearly logged.
- No per-note automatic fallback.
- Use staged vault, not the only live vault.

### 33.3 Final 1.2.0

- Official engine only.
- Community package removed.
- Migration/audit report complete.
- Original vault backup retained.
- Previous portable ZIP retained.
- Documentation and changelog updated.

### 33.4 Soak criteria

Before final release, exercise the official candidate for a meaningful set of real workflows:

- Open/read many notes.
- Edit prose and headings.
- Paste code.
- Paste screenshot.
- Paste rich article content.
- Edit task.
- Edit table.
- Use Source mode.
- Open snippet and insert snippet.
- Pop out note.
- Search.
- Share HTML.
- Restart server and browser.

Any unexplained file rewrite resets the release gate.

---

## 34. Rollback plan

### 34.1 Application rollback

Keep the previous portable ZIP and its version/hash. Rollback replaces program files only and points to the original pre-migration vault backup.

### 34.2 Vault rollback

Preferred rollback uses the untouched original vault directory, not reverse conversion of official output.

### 34.3 Candidate fallback

During candidate testing only, legacy mode may be used to confirm whether a failure is engine-specific. It must not save over a note already changed by the official engine unless the cross-engine downgrade test passed for that note.

### 34.4 Downgrade test

For every canonical output category, test whether the previous release can open it without loss. If it cannot, document the incompatibility and require vault rollback together with application rollback.

### 34.5 Abort conditions

Abort cutover and restore original files if any of these occur:

- Missing note or asset.
- Unexplained source-only increase.
- Task metadata loss.
- Code content change.
- Broken internal link increase.
- Control characters in output.
- Authentication regression.
- Save loop or unexpected PUT on open.
- History/restore failure.
- Previous backup or rollback steps fail verification.

---

## 35. Documentation and repository updates

Required documentation changes:

### 35.1 ADR

Create:

`docs/decisions/0008-official-tiptap-markdown.md`

It must record:

- Why the community package is being removed.
- Why official Beta status is acceptable only with Jotdex guards.
- Exact version baseline.
- Dialect decisions.
- Custom syntax/extensions.
- Raw HTML/Source policy.
- Comment and task metadata handling.
- Table and soft-break guards.
- Migration and rollback design.
- Future dependency-update gate.

### 35.2 Changelog

Append entries explaining:

- The engine replacement.
- User-visible canonicalization, if any.
- Vault audit/migration behavior.
- Source-only behavior.
- Testing and rollback protections.

### 35.3 Vault format

Update `docs/vault-format.md` with the canonical callout, style span, task comment, unresolved wikilink, and unsupported-content rules.

### 35.4 AGENTS and Cursor rules

Update agent guidance so future agents:

- Use `@tiptap/markdown` only.
- Use typed content insertion helpers.
- Add parse/render hooks for persistent custom extensions.
- Run the dialect suite for formatting changes.
- Never upgrade Tiptap packages casually.

### 35.5 AI prompt

Update `src/Web/src/jotdexAiPrompt.ts` when canonical Markdown guidance changes, as required by existing repository rules.

### 35.6 README and upgrading docs

Document:

- The new official Markdown engine.
- Whether a vault audit is recommended.
- Backup and rollback steps.
- Notes that may open Source-only.
- No requirement for Node.js on the production PC.

### 35.7 Notices

Update third-party notices and licenses.

---

## 36. Expected code changes

Exact names may vary, but the final implementation should contain equivalent responsibilities.

### Add

- `src/Web/src/editor/markdown/OfficialMarkdownCodec.ts`
- `src/Web/src/editor/markdown/MarkdownDialectInspector.ts`
- `src/Web/src/editor/markdown/softBreakNormalizer.ts`
- `src/Web/src/editor/operations/contentInsertion.ts`
- `src/Web/src/editor/extensions/JotdexBlockImageMarkdown.ts`
- `src/Web/src/editor/extensions/JotdexTextStyleMarkdown.ts`
- `src/Web/src/editor/extensions/JotdexCalloutMarkdown.ts`
- `src/Web/src/editor/extensions/JotdexTaskMetadata.ts`
- `src/Web/src/editor/extensions/RawHtmlComment.ts`
- `src/Web/src/editor/extensions/UnresolvedWikiLink.ts`, if needed
- `src/Web/src/editor/tables/tableCompatibility.ts`
- `src/Web/src/editor/markdown/schemaCoverage.test.ts`
- `src/Web/src/editor/operations/contentInsertionAudit.test.ts`
- `src/Web/e2e/auth.setup.ts`
- `src/Web/e2e/official-markdown.spec.ts`
- `tools/MarkdownEngineMigration/**`
- `docs/decisions/0008-official-tiptap-markdown.md`
- Migration fixtures and reports outside personal-content paths

### Modify

- `src/Web/package.json`
- `src/Web/package-lock.json`
- `src/Web/src/NoteEditor.tsx`
- `src/Web/src/editor/extensions/createEditorExtensions.ts`
- `src/Web/src/editor/markdown/EditorMarkdownCodec.ts`
- `src/Web/src/editor/markdown/saveSafetyValidator.ts`
- `src/Web/src/editor/markdown/semanticCompare.ts`
- `src/Web/src/editor/testing/createTestEditor.ts`
- `src/Web/playwright.config.ts`
- `src/Web/src/unsafeMarkdown.ts`
- `src/Web/src/callout.ts`
- `src/Web/src/jotdexAiPrompt.ts`
- Templates where compatibility requires it
- Export/render code if canonical callouts need support
- `AGENTS.md`, `STATUS.md`, `CHECKLIST.md`, docs, notices

### Remove before final release

- Legacy package import/use.
- `LegacyMarkdownCodec`.
- `tiptap-markdown` dependency.
- Legacy-only `addStorage().markdown` serializers.
- Legacy engine switch.
- Any temporary comparison UI.

---

## 37. Forbidden shortcuts

The coding agent must not:

- Replace only the import and hope tests catch everything.
- Run both Markdown extensions in one editor.
- Keep `tiptap-markdown` forever as a fallback.
- Add a test-only auth bypass to production code.
- Disable password protection in the real installation.
- Run mutating E2E tests against the only live vault.
- Bulk-save every note through the ordinary PUT API.
- Update every note's `modified` date during cosmetic migration.
- Delete or ignore Jotdex task comments because official comments are unsupported.
- Convert callouts to plain blockquotes without type information.
- Drop color or font size.
- Flatten code, lists, or multi-block table cells into prose without explicit review.
- Trust text-only comparison as proof of structural parity.
- Treat `sameMarkdown()` as sufficient migration validation.
- Use a global regex over Markdown that also touches code fences.
- Suppress official parser errors and save partial output.
- Silently fall back to legacy per note.
- Add a new database-only canonical format.
- Commit personal vault content, credentials, auth state, or migration reports containing note bodies.
- Declare success based only on unit tests without authenticated real-browser/disk tests.

---

## 38. Required verification commands

The coding agent must run the repository's actual commands and update names if scripts change. At minimum, execute equivalents of:

```powershell
cd src\Web
npm ci
npm ls @tiptap/markdown
npm ls tiptap-markdown
npm run lint
npm run test
npm run build
npm run test:e2e

cd ..\..
dotnet test Jotdex.sln
.\scripts\publish-win-x64.ps1
```

Also:

- Start the published portable build without a global Node runtime dependency.
- Probe `/api/health`.
- Run authenticated note open/edit/save/reload.
- Run migration audit and verify commands against the staged real-vault copy.
- Run integrity scan and reindex.

The final `npm ls tiptap-markdown` should report no installed package.

---

## 39. Required agent completion report

The coding agent's final report must include:

1. Baseline commit and final commit.
2. Old and new package versions.
3. Confirmation that all Tiptap packages are exact and aligned.
4. Files added, modified, and removed.
5. Custom dialect handlers implemented.
6. Test commands and exact pass/fail counts.
7. Browser projects tested.
8. Authentication method used for E2E.
9. Confirmation that no auth bypass was added.
10. Real-vault audit totals by classification.
11. Number of notes changed in staged migration.
12. Number of Source-only and unsafe notes with reasons.
13. Link and asset validation totals.
14. Backup paths and manifest hashes.
15. Rollback steps actually tested.
16. Performance comparison.
17. Portable artifact path, version, and SHA-256.
18. Confirmation that the legacy package is absent.
19. Any unresolved risk stated plainly.

Do not report a general statement such as `tests pass` without the command output summary.

---

## 40. Final acceptance checklist

### Architecture

- [ ] Official `@tiptap/markdown` is the sole runtime Markdown engine.
- [ ] Legacy package is absent from final dependency tree.
- [ ] Central codec remains the only parse/serialize boundary.
- [ ] Typed content helpers eliminate ambiguous string insertion.
- [ ] Persistent schema coverage test is green.

### Content safety

- [ ] Front matter preserved.
- [ ] Task/todo comments preserved.
- [ ] Generic comments preserved or Source-only.
- [ ] Callouts preserve type and content.
- [ ] Color and font size preserve.
- [ ] Code is exact.
- [ ] Images and asset paths preserve.
- [ ] Tables emit no control characters.
- [ ] Soft/hard breaks meet Jotdex semantics.
- [ ] Unsupported HTML does not enter lossy visual mode.
- [ ] Unresolved wikilinks preserve.

### Editor behavior

- [ ] No PUT on open.
- [ ] No document reload from attachment metadata.
- [ ] Paste sessions remain transactional.
- [ ] Pending uploads cannot save.
- [ ] Saved status acknowledges exact revision.
- [ ] Conflicts remain visible.
- [ ] History restore works.

### Vault migration

- [ ] Raw backup and SHA manifest exist.
- [ ] Audit is read-only.
- [ ] Staged vault is complete.
- [ ] Every changed note has a reason.
- [ ] Unsafe notes are not rewritten.
- [ ] Assets and links validate.
- [ ] Staged vault passes re-audit, reindex, integrity, export, restart, and browser tests.
- [ ] Rollback was exercised.

### Authentication

- [ ] E2E server is password protected.
- [ ] Anonymous access is denied.
- [ ] API login and UI login are tested.
- [ ] Secrets are not logged or committed.
- [ ] No bypass/backdoor exists.
- [ ] Mutating tests use an isolated/staged vault.

### Release

- [ ] Clean `npm ci` succeeds.
- [ ] Lint succeeds.
- [ ] Frontend tests succeed.
- [ ] .NET tests succeed.
- [ ] Chromium, Firefox, and WebKit E2E succeed.
- [ ] Portable publish succeeds.
- [ ] Portable app runs without Node installed.
- [ ] Docs, ADR, changelog, AI prompt, and notices are updated.
- [ ] Previous portable release and original vault backup are retained.

---

## 41. Definition of done

This project is done when Jotdex uses the official maintained Tiptap Markdown package in production, the legacy package is completely removed, existing Jotdex content has been audited and safely staged, supported notes round-trip without semantic loss, unsupported notes are protected in Source mode, the password-protected real application is tested through normal authentication, and both application and vault can be rolled back using verified recovery artifacts.

A package import change, a successful build, or a few hand-tested notes is not completion.


---

# Appendix A - Reference implementation scaffolds

These are implementation shapes, not copy/paste substitutes for tests. The agent may adapt types to the exact installed Tiptap version.

## A.1 Official extension configuration

```ts
import { Markdown } from '@tiptap/markdown'

export function createOfficialMarkdownExtension(indentSize: number) {
  return Markdown.configure({
    indentation: {
      style: 'space',
      size: indentSize,
    },
    markedOptions: {
      gfm: true,
      breaks: false,
      pedantic: false,
    },
  })
}
```

Do not add this extension alongside the community `Markdown` extension.

## A.2 Typed content helpers

```ts
import type { Editor, JSONContent } from '@tiptap/core'

export function setMarkdownDocument(
  editor: Editor,
  markdown: string,
  options: { emitUpdate?: boolean } = {},
): boolean {
  return editor.commands.setContent(markdown, {
    contentType: 'markdown',
    emitUpdate: options.emitUpdate ?? false,
  })
}

export function insertMarkdown(editor: Editor, markdown: string): boolean {
  return editor.commands.insertContent(markdown, {
    contentType: 'markdown',
  })
}

export function insertHtml(editor: Editor, html: string): boolean {
  return editor.commands.insertContent(html, {
    contentType: 'html',
  })
}

export function insertLiteralText(editor: Editor, text: string): boolean {
  const normalized = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
  const { from, to } = editor.state.selection
  editor.view.dispatch(editor.state.tr.insertText(normalized, from, to).scrollIntoView())
  return true
}

export function replaceWithJson(
  editor: Editor,
  json: JSONContent,
  options: { emitUpdate?: boolean } = {},
): boolean {
  return editor.commands.setContent(json, {
    emitUpdate: options.emitUpdate ?? false,
  })
}
```

The actual helper should integrate Jotdex operation metadata and reload-reason tracking.

## A.3 Official codec outline

```ts
export class OfficialMarkdownCodec implements EditorMarkdownCodec {
  readonly engine = 'official' as const
  private readonly editor: Editor

  constructor(extensions: Extensions) {
    this.editor = new Editor({
      extensions,
      content: '',
      contentType: 'markdown',
    })
  }

  parse(source: string): ParseResult {
    const inspection = inspectJotdexMarkdown(source)
    if (!inspection.visualSafe) {
      return {
        ok: false,
        sourceOnly: true,
        diagnostics: inspection.diagnostics,
      }
    }

    const preprocessed = preprocessJotdexMarkdown(source)
    const json = this.editor.markdown?.parse(preprocessed.markdown)
    if (!json) {
      return failure('official-parse-unavailable')
    }

    const normalized = normalizeOfficialParsedJson(json, preprocessed)
    const validation = validateParsedDocument(normalized)
    if (!validation.ok) return validation

    return {
      ok: true,
      json: normalized,
      diagnostics: [...preprocessed.diagnostics, ...validation.diagnostics],
    }
  }

  serialize(doc: PmNode): SerializeResult {
    const transient = findTransientNodes(doc)
    if (transient.length) {
      return failure('transient-node-present')
    }

    const raw = this.editor.markdown?.serialize(doc.toJSON())
    if (raw == null) return failure('official-serialize-unavailable')

    const restored = restoreProtectedMarkdown(raw)
    const safety = validateMarkdownSafety(restored)
    if (safety.length) {
      return { ok: false, diagnostics: safety }
    }

    const reparsed = this.parse(restored)
    if (!reparsed.ok) return reparsed

    const semantic = compareSemanticDocuments(doc.toJSON(), reparsed.json)
    if (!semantic.equal) {
      return {
        ok: false,
        diagnostics: semantic.diagnostics,
      }
    }

    return { ok: true, markdown: restored, diagnostics: [] }
  }

  destroy(): void {
    this.editor.destroy()
  }
}
```

Do not call `setContent()` in a way that emits live editor updates just to serialize a document.

## A.4 Block-image paragraph parser sketch

```ts
const JotdexBlockImage = Image.extend({
  name: 'image',

  markdownTokenName: 'paragraph',

  parseMarkdown(token, helpers) {
    const tokens = token.tokens ?? []
    const meaningful = tokens.filter(t => !(t.type === 'text' && !String(t.text ?? '').trim()))
    if (meaningful.length !== 1 || meaningful[0]?.type !== 'image') {
      return null
    }

    const image = meaningful[0]
    return helpers.createNode('image', {
      src: image.href ?? '',
      alt: image.text ?? '',
      title: image.title ?? null,
    })
  },

  renderMarkdown(node) {
    return renderCanonicalBlockImage(node.attrs)
  },
})
```

Because the extension handles the `paragraph` token name, verify handler priority and fallback behavior with the exact official manager version. A mixed paragraph must return `null` so the normal paragraph handler gets a chance.

## A.5 Task metadata tokenizer sketch

```ts
const TASK_COMMENT = /^<!--\s*jotdex-(task|todo)\s+([\s\S]*?)-->/i

const JotdexTaskMetadata = Node.create({
  name: 'jotdexTaskMetadata',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: false,

  addAttributes() {
    return {
      kind: { default: 'task' },
      raw: { default: '' },
      id: { default: null },
      priority: { default: null },
      due: { default: null },
      remind: { default: null },
      unknown: { default: null },
    }
  },

  markdownTokenName: 'jotdexTaskMetadata',

  markdownTokenizer: {
    name: 'jotdexTaskMetadata',
    level: 'inline',
    start: '<!--',
    tokenize(src) {
      const match = TASK_COMMENT.exec(src)
      if (!match) return undefined
      return {
        type: 'jotdexTaskMetadata',
        raw: match[0],
        kind: match[1].toLowerCase(),
        attrsRaw: match[2],
      }
    },
  },

  parseMarkdown(token, helpers) {
    const parsed = parseTaskComment(token.raw)
    return helpers.createNode('jotdexTaskMetadata', parsed)
  },

  renderMarkdown(node) {
    return renderTaskComment(node.attrs)
  },
})
```

The exact parser must preserve unknown attributes and must ensure the rendered comment stays on the checklist line.

## A.6 Callout tokenizer sketch

```ts
const CALLOUT_START = /^>\s*\[!(NOTE|TIP|INFO|WARNING|DANGER)\]\s*(?:\n|$)/i

const JotdexCallout = Callout.extend({
  markdownTokenName: 'jotdexCallout',

  markdownTokenizer: {
    name: 'jotdexCallout',
    level: 'block',
    start: '> [!',
    tokenize(src, _tokens, helpers) {
      const parsed = consumeCalloutBlock(src)
      if (!parsed) return undefined
      return {
        type: 'jotdexCallout',
        raw: parsed.raw,
        calloutType: parsed.type,
        tokens: helpers.blockTokens(parsed.innerMarkdown),
      }
    },
  },

  parseMarkdown(token, helpers) {
    return helpers.createNode(
      'callout',
      { type: String(token.calloutType).toLowerCase() },
      helpers.parseBlockChildren(token.tokens ?? []),
    )
  },

  renderMarkdown(node, helpers) {
    const type = String(node.attrs?.type ?? 'note').toUpperCase()
    const body = helpers.renderChildren(node.content ?? []).trimEnd()
    const quoted = body
      .split('\n')
      .map(line => (line.length ? `> ${line}` : '>'))
      .join('\n')
    return quoted ? `> [!${type}]\n${quoted}` : `> [!${type}]`
  },
})
```

`consumeCalloutBlock()` must be a real block parser, not a regex that stops at the first blank line.

## A.7 Playwright authenticated setup sketch

```ts
import { test as setup, expect } from '@playwright/test'
import { readFile } from 'node:fs/promises'

const authFile = 'playwright/.auth/jotdex.json'

setup('authenticate', async ({ request }) => {
  const status = await request.get('/api/auth/status')
  expect(status.ok()).toBeTruthy()
  const auth = await status.json()

  if (!auth.passwordSet && process.env.JOTDEX_E2E_MODE === 'ephemeral') {
    const password = process.env.JOTDEX_E2E_GENERATED_PASSWORD
    if (!password) throw new Error('Ephemeral test password was not generated')

    const created = await request.post('/api/auth/setup', {
      data: {
        username: 'admin',
        password,
        displayName: 'Jotdex E2E',
      },
    })
    expect(created.ok()).toBeTruthy()
  } else if (auth.authRequired && !auth.authenticated) {
    const password = process.env.JOTDEX_E2E_PASSWORD_FILE
      ? (await readFile(process.env.JOTDEX_E2E_PASSWORD_FILE, 'utf8')).trimEnd()
      : process.env.JOTDEX_E2E_PASSWORD

    if (!password) throw new Error('Protected Jotdex E2E credentials are not configured')

    const login = await request.post('/api/auth/login', {
      data: {
        username: process.env.JOTDEX_E2E_USERNAME ?? 'admin',
        password,
        totpCode: await getConfiguredTotpOrRecoveryCode(),
      },
    })
    expect(login.ok()).toBeTruthy()
  }

  await request.storageState({ path: authFile })
})
```

The real implementation must redact secrets and avoid attaching request bodies containing passwords to traces.

## A.8 Migration manifest example

```json
{
  "schemaVersion": 1,
  "runId": "2026-09-01T20-30-00Z-ab12cd34",
  "sourceVault": "C:/JotdexVault",
  "sourceVaultMarkerHash": "...",
  "legacyPackage": "tiptap-markdown@0.9.0",
  "officialPackage": "@tiptap/markdown@3.29.2",
  "files": [
    {
      "relativePath": "Technical/Example.md",
      "beforeSha256": "...",
      "afterSha256": "...",
      "classification": "canonical",
      "transformIds": ["CLEAN-01"],
      "features": {
        "headings": 3,
        "images": 1,
        "codeBlocks": 2,
        "taskComments": 0,
        "callouts": 0,
        "tables": 0
      },
      "semanticBefore": "...",
      "semanticAfter": "...",
      "assetErrors": [],
      "linkErrors": [],
      "diagnostics": []
    }
  ]
}
```

Do not store credentials or full note bodies in the manifest.

---

# Appendix B - Authoritative implementation references

The coding agent should consult the current versions of these sources while implementing:

## Official Tiptap sources and documentation

- Tiptap Markdown Introduction and limitations.
- Tiptap Markdown Basic Usage.
- Tiptap Markdown Extension API.
- Tiptap guide for integrating Markdown in custom extensions.
- Tiptap guide for custom parsing/tokenizers.
- `packages/markdown/src/Extension.ts` in the exact pinned Tiptap tag.
- `packages/markdown/src/MarkdownManager.ts` in the exact pinned tag.
- `packages/core/src/Extendable.ts` for `parseMarkdown`, `renderMarkdown`, `markdownTokenizer`, and `markdownOptions`.
- Official Image extension parse/render implementation.
- Official table Markdown utilities.
- Official TextStyle and Color extensions.

## Official issue guards current at contract creation

- `ueberdosis/tiptap#8136`: CommonMark soft line breaks exposed as literal newlines in affected versions.
- `ueberdosis/tiptap#8152`: U+001F table-cell separator can leak into output for multi-block cells.
- `ueberdosis/tiptap#8116`: HTML comment inside heading can create invalid content/crash in browser conditions.
- `ueberdosis/tiptap#7958`: heading inline marks after ordered lists, fixed in later versions but retained as a regression test.
- Historical task-list, hard-break, blank-line, table-alignment, and overlapping-mark issues relevant to the selected exact version.

## Jotdex sources

- Current `EditorMarkdownCodec` and all 1.1.24 reliability modules.
- Current `createEditorExtensions` and persistent custom extensions.
- Current auth endpoints and cookie behavior.
- Current vault/task services.
- Existing RoundTripFixtures, editor tests, and E2E tests.
- Repository ADR and changelog requirements.

The issue list is a regression input, not evidence that the official package should be rejected. The maintained package is still the correct target, provided Jotdex owns the dialect and guards its canonical files.

