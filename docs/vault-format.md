# Jotdex vault format

The vault is the product. Jotdex is a replaceable UI over these files.

## Root

```text
JotdexVault/
├── .notes-vault.json
├── Personal/
└── Technical/
```

Suggested live path on this host: `C:\JotdexVault` or `D:\JotdexVault` (**local disk**, not iCloud).

### `.notes-vault.json`

```json
{
  "id": "uuid",
  "formatVersion": 1,
  "name": "Jotdex Vault",
  "created": "2026-08-06T00:00:00Z"
}
```

## OneNote → filesystem mapping

| OneNote | Filesystem |
|---|---|
| Notebook | Top-level folder |
| Section group | Folder |
| Section | Folder |
| Page | `Title.md` |
| Images / attachments | Sibling `Title.assets\` |
| Subpage | Front matter `parent_id`, `subpage_order` |

## Note file

UTF-8 Markdown with optional YAML front matter:

```markdown
---
id: 6e1df781-8b61-4d49-919c-48ab469fbc11
title: OPNsense IPsec VPN
created: 2025-03-27T09:15:00Z
modified: 2026-08-06T14:10:00Z
tags:
  - networking
  - vpn
source: onenote
parent_id: null
subpage_order: null
---

# OPNsense IPsec VPN

Body…
```

Rules:

- Front matter optional for hand-written notes; assign `id` on first app-managed save.
- Preserve unknown front-matter keys.
- Display title order: front-matter `title` → first H1 → filename.
- Preserve `created` when present; app may update `modified`.
- No BOM unless an imported file requires preservation.

### Vault root `Todos.md`

Optional notebook-wide to-do list used by the UI rail/tab. Open items only:

```markdown
- [ ] Call dentist <!-- jotdex-todo id="…" priority="high" due="2026-08-10T15:00:00Z" remind="every:30m" -->
```

Done items are deleted from the file (not kept as `- [x]`). Attributes: `id`, `priority` (`low`|`normal`|`high`|`critical`), optional `due` (ISO-8601), `remind` (`off`|`once:ISO`|`every:30m`|`every:1h`).

### Reserved `Snippets/` folder

Reusable code snippets are ordinary Markdown files under `Snippets/` with:

```yaml
jotdex_type: code-snippet
jotdex_language: powershell
jotdex_trigger: restart-spooler
```

The body holds a fenced code block (the insertable text). Jotdex hides this folder and these notes from the notes list / folders rail; use **Insert snippet** or Ctrl+Space in the code **Edit** dialog. Files remain editable on disk like any other vault note.

## Attachments

```text
OPNsense IPsec VPN.md
OPNsense IPsec VPN.assets/
  ├── 2026-08-06_141522_firewall-error.png
  └── clipped-page.html
```

Relative Markdown links only. **Never** base64-embed normal images.

Screenshot auto-name: `yyyy-MM-dd_HHmmss_descriptor.ext`.

## HTML sidecars (Markdown-plus-assets)

For complex webpage/OneNote fidelity:

1. Best-effort Markdown in the note body.
2. Sanitized HTML snapshot in `.assets/` (e.g. `clipped-page.html`).
3. Embed as expandable section or attachment link.
4. File remains usable in any browser without Jotdex.

## Windows filenames

Sanitize invalid chars `< > : " / \ | ? *`, reserved names (`CON`, `PRN`, …), trailing dots/spaces. Keep full title in front matter. Collisions get a visible suffix — never silent overwrite.

## Internal links

Prefer relative Markdown links. Unresolved `[[wikilinks]]` are preserved, not deleted.

## Editor Markdown dialect (1.2.0)

The visual editor round-trips through official `@tiptap/markdown` plus Jotdex handlers. On-disk form:

**Callouts** — Obsidian syntax is canonical:

```markdown
> [!warning]
> Do not flatten this to a typeless quote.
```

Types: `note`, `tip`, `info`, `warning`, `danger`. Older HTML `<blockquote data-callout="…">` still parses when present.

**Color / size** — limited spans only (not arbitrary CSS):

```markdown
<span style="color: #c47b2b">warning text</span>
<span style="font-size: 1.25em">larger</span>
```

**Task / inbox metadata** — HTML comments the backend already indexes. Do not strip them:

```markdown
- [ ] Call vendor <!-- jotdex-task id="…" due="2026-08-20T15:00:00.000Z" priority="high" remind="every:30m" -->
```

`Todos.md` uses `<!-- jotdex-todo … -->` (see above).

**Wikilinks** — unresolved `[[Title]]` stay in the file until a real note exists.

**Images** — vault-relative `![alt](Note title.assets/file.png)`. A paragraph that is only an image is a block image; the renderer closes the Markdown block so `![x](url)` cannot fuse onto the next `###` heading. Image width/height are not stored. A paragraph that mixes an image with prose is **Source-only** (not silently flattened).

**Unsupported content** — complex raw HTML, `javascript:` URLs, multi-block table cells, and other shapes the dialect cannot represent open in **Source** with a reason. Nothing is dropped on save from Source. Transient upload placeholders are never written to disk.

**Soft breaks** — a newline inside a normal text node is treated as a space. Fenced code is not rewritten.

## Editor Markdown dialect v2 (1.3.0)

Additive on top of 1.2.x. 1.2.2 Source can still *read* these files as text; visual features need 1.3.0. Do not bulk-rewrite old notes. Malformed v2 markers open Source-only.

**Highlight** — `==text==`. Pasted `<mark>` normalizes after a real edit.

**Underline / sub / sup** — `<u>`, `<sub>`, `<sup>`.

**Alignment** — HTML comment immediately before the next top-level paragraph or heading. Left = no marker.

```markdown
<!-- jotdex-align: center -->
Centered paragraph.
```

**Math** — `\(...\)` inline, `\[...\]` block. Never auto-convert `$` / `$$`.

**Emoji** — Unicode character only.

**Details** — first block is the summary. Open/closed is not saved.

```markdown
<!-- jotdex-details -->
Summary
Body
<!-- /jotdex-details -->
```

**Callouts** — still `> [!type]`. Optional title on the marker line. `-` / `+` after `]` is the default collapsed/expanded state. Live toggle does not dirty the note.

```markdown
> [!warning] Prod change
> Body

> [!tip]- Extra
> Hidden by default
```

**Images** — standard `![alt](Note.assets/file.png)`. `<figure>` only when caption, width, or alignment is set. Runtime `blob:` URLs are never saved.

**Bookmark cards**

```markdown
<!-- jotdex-link-card -->
[Example](https://example.com)
```
