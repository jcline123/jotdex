# Backup bundles

Jotdex keeps **notes on disk in the vault**. App data is separate. A backup should always include the vault; app data is optional.

## Minimum backup (recommended)

1. Stop Jotdex (or the Windows Service).
2. Copy the entire **vault** folder (all `.md` and `.assets`).
3. Optionally copy:
   - `data\config\` (vault path + network settings)
   - `data\auth\` (admin hash — treat as secret)
   - `data\history\` (note snapshots)
4. Resume Jotdex.

Do **not** rely on `data\indexes\` — it is rebuilt on Rescan/start.

## In-app backup ZIP

**Settings → Maintenance → Create backup ZIP** (or `POST /api/admin/backup`) writes:

`data\exports\backups\jotdex-backup-YYYYMMDD-HHMMSS.zip`

Contents:

| Entry | Purpose |
|---|---|
| `vault/` | Full live vault copy |
| `appdata/config/` | Network, vault path, mirror settings |
| `appdata/auth/` | Admin credentials (included by default) |
| `appdata/history/` | Note history (included by default) |
| `MANIFEST.json` | Timestamp + flags |

Query flags: `?includeAuth=false` / `?includeHistory=false` to omit secrets or history.

Indexes are never included. Treat auth-containing ZIPs as secrets.

## Locations

| Mode | App data root |
|---|---|
| Portable | `.\data` beside `Jotdex.Server.exe` |
| Service / normal | `%LOCALAPPDATA%\Jotdex` |

## Restore

1. Install/publish Jotdex.
2. Restore the vault folder to local disk (not iCloud live).
3. Restore `data\auth` / `data\config` if you want the same admin and network settings.
4. Start → open UI → confirm vault path → Rescan.

## Static HTML export

**Settings / top bar → Export HTML** writes a read-only site to `data\exports\static\`.

- Open `index.html` (or serve the folder with any static server for search).
- Contains rendered notes, copied `.assets`, and `search\index.json`.
- No auth secrets; scripts in note HTML are stripped at export time.

This is a safety copy, not the primary editor.
