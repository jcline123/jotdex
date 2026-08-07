# OneNote MD export inventory

**Source (unchanged):** `…\OneNoteMdExporter…\Exports\md` (local export path)  
**Exporter:** [alxnbl/onenote-md-exporter](https://github.com/alxnbl/onenote-md-exporter) v1.6.0  
**Inventoried:** 2026-08-06  
**Destination vault:** local disk e.g. `C:\JotdexVault`

## Layout (exporter)

Each notebook export folder looks like:

```text
Work-20260806 15-27\
├── resources\          # shared binary pool (png/docx/…)
└── Work\               # notebook name
    ├── Install Notes\  # section → folder
    │   └── Page.md
    └── Knowledge Base\
```

Image links in Markdown:

```markdown
![image1](../../resources/000ecf5bb07f41eaa61afd9b871efc98.png)
```

Front matter from exporter typically includes `title`, `created`, `updated` (no `id`).

## Counts

| Export folder | Notes | Resources | Notes |
|---|---:|---:|---|
| `Joshua's Notebook-20260806 15-07` | 0 | — | Empty / skipped |
| `Joshua's Notebook-20260806 15-25` | 30 | 52 | Personal |
| `Work-20260806 15-27` | 584 | 496 | Work |

**Total migrated:** 614 notes, 526 images + 10 other attachments copied into sibling `.assets` folders. **Missing resources:** 0.

## Jotdex mapping

| Exporter | Jotdex vault |
|---|---|
| Notebook folder | Top-level folder (`Work`, `Joshua's Notebook`) |
| Section | Subfolder |
| `Page.md` | `Title.md` (+ sanitized Windows name) |
| `resources/hash.ext` | `Title.assets/hash.ext` + rewritten relative link |
| FM title/created/updated | Preserved; added `id`, `source: onenote` |

## Quirks observed

- Some titles contain HTML entities / control chars (e.g. `&#11;`) — sanitized for filenames; full title kept in front matter.
- ~141 Work notes contain HTML tags (tables/divs) — preserved as-is; dense HTML may open in Source mode.
- Shared `resources/` pool means the same hash can appear in multiple notes; each note gets its own copy under `.assets` (simple, link-safe).

## Tool

```powershell
cd jotdex\tools\MigrateExport
.\Migrate-OneNoteMdExporter.ps1 `
  -SourceRoot "C:\Users\…\Exports\md" `
  -Destination "C:\JotdexVault"
```

Use `-DryRun` first. Reports land in `docs/import-format/migration-report-*.md`.
