# Importing from OneNote (onenote-md-exporter)

Jotdex does **not** import OneNote inside the app (see ADR 0002). Use an offline export, then migrate once into a local-disk vault.

## 1. Export with alxnbl/onenote-md-exporter

1. Install/run [alxnbl/onenote-md-exporter](https://github.com/alxnbl/onenote-md-exporter) (Windows; talks to the local OneNote desktop app).
2. Export notebooks to Markdown. Typical output:

```text
Exports\md\
├── Work-YYYYMMDD …\
│   ├── resources\     # images & attachments
│   └── Work\          # sections → folders, pages → .md
└── Personal-…\
    ├── resources\
    └── …
```

3. Keep that folder as a read-only archive. **Do not** edit it in place.

## 2. Migrate into a Jotdex vault

```powershell
cd jotdex\tools\MigrateExport

# Preview
.\Migrate-OneNoteMdExporter.ps1 `
  -SourceRoot "C:\Users\YOU\Downloads\OneNoteMdExporter…\Exports\md" `
  -Destination "C:\JotdexVault" `
  -DryRun

# Commit (creates C:\JotdexVault; never writes back to the export)
.\Migrate-OneNoteMdExporter.ps1 `
  -SourceRoot "C:\Users\YOU\Downloads\OneNoteMdExporter…\Exports\md" `
  -Destination "C:\JotdexVault"
```

What the tool does:

- Maps notebook → top-level folder, section → subfolder, page → `Title.md`
- Copies each referenced file from `resources\` into sibling `Title.assets\`
- Rewrites `../../resources/…` links to `Title.assets/…`
- Adds stable `id` + `source: onenote` front matter
- Writes a report under `docs/import-format/`

## 3. Point Jotdex at the vault

1. Prefer **local disk** (`C:\JotdexVault`), not iCloud.
2. Start Jotdex → **Settings** → set vault path → Rescan / reindex.
3. Spot-check search, a note with images, and **Integrity scan**.

See also: [export-inventory.md](export-inventory.md), [vault-format.md](../vault-format.md), [vault-mirror.md](../vault-mirror.md).
