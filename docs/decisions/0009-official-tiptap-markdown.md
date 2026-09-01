# ADR 0009: Official Tiptap Markdown engine

## Status

Accepted (Jotdex 1.2.0)

## Context

1.1.24 kept community `tiptap-markdown@0.9.0` as the lower-level converter behind `EditorMarkdownCodec`. That package is unmaintained for Tiptap 3; the maintainer points at official `@tiptap/markdown`. Official is still a Beta surface: stock parse/serialize drops Jotdex-owned syntax (colors, callout types, `<!-- jotdex-task -->` / `<!-- jotdex-todo -->`, unresolved `[[wikilinks]]`, generic HTML comments).

This is an engine swap, not an editor rewrite. Reliability from ADR 0008 stays: codec-only parse/serialize, paste sessions, no attachment `setContent()`, revision-aware autosave, save-safety, whole-block headings, exact code paste.

## Decision

1. **Official only.** Production uses `@tiptap/markdown` **3.29.2**, pinned exactly (no caret) with every other `@tiptap/*` package. `tiptap-markdown` is removed. There is no per-note or permanent legacy fallback.
2. **One Markdown extension per editor.** Parse via `editor.markdown.parse` / `contentType: 'markdown'`. Serialize via `editor.getMarkdown()`. Never `storage.markdown.getMarkdown()`.
3. **Jotdex owns the dialect.** Official handlers (not global regex over fences) cover block images, Obsidian callouts, limited style spans, task/todo comments, raw HTML comments, unresolved wikilinks, table compatibility, and soft-break normalization. Mixed inline image+prose and other unsupported shapes force Source-only.
4. **Typed insertion.** `setMarkdownDocument` / `insertMarkdown` / `insertHtml` / `insertLiteralText` / `replaceWithJson` are the only production string/JSON content writes.
5. **List indent is 2 spaces.** Recorded from official default, GFM, SampleVault list/YAML spacing, and nested lists in the `C:\JotdexMigration\backup` copy of the live vault — not “because the package default exists.”
6. **Beta is acceptable only with guards.** Schema-coverage, OFF-01–OFF-10, reliability matrix, and `markdown:migrate` audit on a vault *copy*. Live `C:\JotdexVault` is never the audit/stage/apply target unless Joshua explicitly approves apply after reviewing the report.
7. **Future Tiptap upgrades are a gate, not a casual bump.** Re-pin every `@tiptap/*` to the same exact version, re-run the dialect suite and a vault-copy audit, and update this ADR if parse/serialize behavior changes.

## Compatibility (stock official vs Jotdex dialect)

Isolated spike (`officialSpike.test.ts` / `C:\JotdexMigration\official-spike.json` when present):

| Construct | Stock `@tiptap/markdown` 3.29.2 | Jotdex dialect |
|---|---|---|
| `![x](url)` then `### H` | Already a separate heading | Same, plus owned block-image render so `)###` cannot fuse |
| `<span style="color/font-size">` | Dropped | `JotdexTextStyle` / `JotdexColor` |
| `<!-- jotdex-task … -->` / `jotdex-todo` | Dropped (comments unsupported) | Persistent `JotdexTaskMetadata` atom; backend still sees the same IDs |
| `> [!warning]` / HTML `data-callout` | Typeless quote | `JotdexCallout`; canonical on-disk form is Obsidian `> [!type]` |
| `[[Missing Note]]` | Dropped or treated as text loss | `UnresolvedWikiLink` preserved |
| Generic `<!-- … -->` | Dropped | Raw comment nodes (brace-rewrite around official parse) |
| Soft newline in a text node | Can survive (OFF-01) | Named normalizer → space; fences skipped |
| Mixed image + prose in one paragraph | Easy to flatten | Source-only (do not silently rewrite) |
| Multi-block table cells / control-char tables | Unsafe | Review / Source-only (OFF-02) |

## Custom syntax

- **Callouts:** write `> [!note]` (also `tip`, `info`, `warning`, `danger`). Older HTML `<blockquote data-callout>` still parses when possible.
- **Task metadata:** HTML comments on list items, unchanged for `VaultTaskService`.
- **Styles:** limited `<span style="color: …">` / `font-size: …em`.
- **Wikilinks:** unresolved `[[…]]` stay in the file.
- **Images:** vault-relative `src`; width/height are not invented as `<img>` attributes. A paragraph that is exactly one image becomes a block image.

## Raw HTML / Source policy

Unsupported Markdown or complex raw HTML is not dropped. The codec marks Source-only with a reason. Transient `pendingAsset` nodes stay save-blocked.

## Migration and rollback

- Dev-only `npm run markdown:migrate` (`tools/MarkdownEngineMigration/`): `audit` (default, read-only), `stage`, `verify`, `rollback`. `apply` is implemented but refuses `C:\JotdexVault` and is not run in this release.
- MDM-06 on `C:\JotdexMigration\backup`: 649 notes — 610 ok, 39 source-only (almost all mixed inline image+prose). Reports stay off-git (personal titles).
- Rollback of the *program* is the previous portable exe (1.1.24). Rollback of a *staged* tree is the SHA-256 backup copy. Do not bulk-PUT the live vault.

## Consequences

- Opening a note without editing must still not rewrite the file.
- Portable 1.2.0 does not require Node.js; the migrate CLI is a development-machine tool.
- GitHub Release waits for an explicit publish.
