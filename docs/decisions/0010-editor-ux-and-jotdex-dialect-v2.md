# ADR 0010: Editor UX and Jotdex dialect v2

## Status

Accepted for Jotdex **1.3.0** (packages confirmed against `@tiptap/*` **3.29.2** where listed below).

## Context

1.2.2 ships official `@tiptap/markdown` 3.29.2, a visual toolbar, heading fold, gap cursor between stacked hard blocks, paste sessions, and Obsidian callouts. It does not ship slash/plus menus, a bubble menu, drag-handle block actions, table chrome, image inspector/figures, link popovers/bookmark cards, Details, highlight/underline/sub/sup, alignment comments, titled/collapsible callouts, a live outline, math, or an emoji picker.

Contract: [`editor-ux-expansion-contract.md`](editor-ux-expansion-contract.md). Baseline portable **1.2.2** (`v1.2.2` @ `559d061`). Files remain the product. New syntax is canonicalized only after a real edit. Live vault `C:\JotdexVault` is never the `markdown:migrate apply` target.

## Decision

1. **Dialect v2 is additive.** Existing 1.2.2 Markdown still opens. New forms are documented in this ADR and [`docs/vault-format.md`](../vault-format.md). Malformed v2 markers force Source-only with a reason. Do not bulk-rewrite old notes.
2. **Official Tiptap only.** Every `@tiptap/*` package stays pinned at **3.29.2**. No community `tiptap-markdown`, Tiptap Cloud, or paid UI/paste handler. If an official extension is missing or pulls Collaboration/Yjs, implement a Jotdex-owned ProseMirror plugin instead of bumping Tiptap.
3. **Command registry first.** Slash, gutter plus, drag menu, toolbar, and shortcuts share one descriptor list (`src/Web/src/editor/commands/`). Overlay coordinator allows one primary editor menu. Selection bookmarks bind async replace/upload to note id + session + mapped positions.
4. **Gap cursor stays.** The 1.2.2 `BlockGapNavigation` extension is the insertion seam. Gutter `+` uses those gaps; it does not replace them.
5. **Open without edit does not PUT.** Visual canonicalize of new syntax happens only after a real edit.

## Dialect v2 on-disk forms

| Feature | On-disk form | Notes |
|---|---|---|
| Highlight | `==text==` | One default color. Pasted `<mark>` normalizes after a real edit. |
| Underline | `<u>text</u>` | HTML mark, not Markdown underscore. |
| Subscript | `<sub>text</sub>` | |
| Superscript | `<sup>text</sup>` | |
| Alignment | `<!-- jotdex-align: center\|right\|justify -->` immediately before the next top-level paragraph or heading | Left = no marker. |
| Inline math | `\(...\)` | Never auto-convert `$`. |
| Block math | `\[...\]` | Never auto-convert `$$` (currency / PowerShell). |
| Emoji | Unicode character only | No shortcode rewrite on disk. |
| Details | `<!-- jotdex-details -->` … `<!-- /jotdex-details -->` | First block is the summary. Open/closed is **not** saved. |
| Callout | `> [!type]` with optional title on the marker line; `-` / `+` for collapsible default | Live toggle does not dirty. Legacy `> [!type]` still round-trips. |
| Image | `![alt](Note.assets/file.png)` | Standard. |
| Figure | `<figure>` only when caption, width, or alignment is set | Runtime blob URLs never saved. |
| Bookmark | `<!-- jotdex-link-card -->` then a normal markdown link | Unsafe schemes blocked. |
| Typography | Off by default | Never inside code, links, or comments. Not persisted as HTML entities beyond ordinary Unicode. |

## Tiptap 3.29.2 package inventory

| Package | Role in 1.3.0 | Decision |
|---|---|---|
| `@tiptap/extension-highlight` | `==` / `<mark>` | Official, pin 3.29.2. HTML `<u>`/`<sub>`/`<sup>` are brace-rewritten before parse so `>` cannot start a blockquote. |
| `@tiptap/extension-underline` | `<u>` | Official (StarterKit disabled; Jotdex handlers) |
| `@tiptap/extension-subscript` | `<sub>` | Official, pin 3.29.2 |
| `@tiptap/extension-superscript` | `<sup>` | Official, pin 3.29.2 |
| `@tiptap/extension-text-align` | Not used | Alignment is HTML comments, not CSS classes on every paragraph |
| `@tiptap/suggestion` | Not used | Slash uses a Jotdex plugin (same pattern as `[[` wiki suggest) |
| `@tiptap/extension-drag-handle` | **Not used** | 3.29.2 vanilla package pulls `@tiptap/extension-collaboration` + **Yjs**. Jotdex-owned handle + Alt+Up/Down instead. |
| `@tiptap/extension-details` | Not used | Jotdex-owned `details` node for `<!-- jotdex-details -->` |
| `@tiptap/extension-mathematics` | **Not used** | Peer `katex@^0.16.4 \|\| ^0.17.0` conflicts with current katex. Jotdex-owned math + **katex 0.16.22**, `trust: false`, `\(` `\)` / `\[` `\]` only |
| `@tiptap/extension-emoji` | **Not used** | That extension stores shortcode nodes. Emoji picker inserts Unicode only. |
| `@tiptap/extension-typography` | Optional | **Off by default** (`localStorage jotdex.typography=1` to enable). Never inside code/links/comments. |
| `@tiptap/react/menus` | BubbleMenu | Open-source menus only |
| `@floating-ui/dom` | Overlay positioning | MIT, **1.7.6** — peer of `@tiptap/extension-floating-menu` |
| `katex` | Math render | Bundled locally, `trust: false`, no CDN |

## Constraints that never move

- Insert via `src/Web/src/editor/operations/contentInsertion.ts`.
- Keep codec / paste session / attachment resolver / revision+save coordinators out of a bloated `NoteEditor.tsx`.
- Same change as each new mark/node: parse/render, `jotdexAiPrompt.ts`, schema coverage, Share/static CSS, search extraction, changelog.
- E2E through normal setup/login on an isolated passworded instance. No Development auth bypass after a password exists.
- Never `markdown:migrate apply` on `C:\JotdexVault`.

## Consequences

- 1.2.2 Source can still *read* new Markdown as text. Visual dialect features need 1.3.0.
- Rollback of the program is the previous portable exe (1.2.2).
- Portable artifact must not contain vault copies or test passwords.

## Appendix A — 1.2.2 baseline inventory

See [`0010-appendix-1.2.2-inventory.md`](0010-appendix-1.2.2-inventory.md).
