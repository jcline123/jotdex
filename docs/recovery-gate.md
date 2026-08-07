# Recovery gate (M7)

Checklist for “can I recover on a fresh Windows PC?”

## Static export

1. In Jotdex: **Export HTML**.
2. Open `data\exports\static\index.html` (or serve that folder).
3. Confirm notes render and assets load.
4. Search works when served over http (not always from `file://`).

## Backup / restore

See [backup.md](backup.md).

1. Stop Jotdex.
2. Copy vault + optional `data\config`, `data\auth`, `data\history`.
3. On a new machine: install portable build, restore vault to local disk, start, set vault path, Rescan.

## Read-only iCloud mirror

See [vault-mirror.md](vault-mirror.md).

1. Live vault stays on local disk.
2. Schedule `scripts\mirror-vault.ps1` to a folder under iCloud Drive.
3. Never point `VaultPath` at the mirror.

## Integrity

**Settings → Maintenance → Integrity scan** reports missing `.assets` files, broken `.md` links, unresolved wikilinks, and orphan asset folders.
