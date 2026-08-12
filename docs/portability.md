# Portability

## Easiest move (another PC)

Use **Settings → Backup → Create move kit (ZIP)**. That package includes your vault, app data, the portable program (when available), and `Restore-Jotdex.ps1`. Full steps: [backup.md](backup.md).

## Move the vault only

1. Stop Jotdex.
2. Copy the entire vault folder (all `.md` and `.assets`).
3. Install Jotdex on the new host (portable ZIP or service).
4. Point config / first-run at the vault path (local disk).
5. Start app → full reindex.
6. Recreate admin account if auth data was not migrated.

Notes travel with the vault. Search index is rebuilt.

## What does not travel with the vault by default

| Data | Location | On vault move |
|---|---|---|
| Notes + assets | Vault | Copied |
| Search index | AppData `indexes\` | Rebuild |
| Note history snapshots | AppData `history\` | Optional / included in move kit |
| Trash | AppData `trash\` | Optional |
| Auth / sessions | AppData `auth\` | Recreate or copy carefully / included in move kit |
| Secrets (SMTP/Telegram/TOTP) | AppData `secrets\secrets.json` (DPAPI) | Move kit unwraps to `secrets-portable.json`; rewrapped on first start |
| Cloud backup settings | AppData `config\cloud-backup.json` | Included in move kit (preferences only) |
| Cloud OAuth credentials | AppData `secrets\cloud-backup.json` (DPAPI) | **Never** in move kits — reconnect providers on the new PC |
| Cloud backup runtime status | AppData `state\cloud-backup\` | Not portable |
| Cloud backup staging | AppData `exports\cloud-backup-staging\` | Not portable |
| Logs | AppData `logs\` | Skip |

Move-kit and backup ZIPs may include `history\`, `auth\`, and portable secrets. Treat those ZIPs as secret. Cloud provider OAuth never travels in kits.

## AppData locations

- Service / normal install: `%LOCALAPPDATA%\Jotdex\`
- Portable: `.\data\` beside the executable

## iCloud (read-only mirror only)

**Do not** run the live vault inside an iCloud-synced directory.

Correct flow:

```text
Authoritative vault (local disk)
        |
        v
Scheduled read-only copy/mirror
        |
        v
iCloud Drive (browse offline; not written by the server)
```

Use `scripts/mirror-vault.ps1` — see [vault-mirror.md](vault-mirror.md).

Away from home: secure remote to web UI, browse iCloud mirror, or open static HTML export.

## Automatic cloud mirror (in-app)

Keep the **live** vault on local disk (e.g. `C:\JotdexVault`). In **Settings → Backup → Cloud backup mirror**, set a destination under iCloud/OneDrive/Dropbox and enable automatic copy.

- One-way only (live → cloud). The app never writes notes into the mirror as the live vault.
- Uses Windows `robocopy /MIR` so the destination matches the vault.
- Do **not** point VaultPath at the mirror folder.

Manual script alternative: `scripts/mirror-vault.ps1` (see [vault-mirror.md](vault-mirror.md)).

## Multi-provider cloud backup (separate)

API uploads of encrypted Move Kits to Dropbox / Google Drive / OneDrive are configured under **Settings → Backup → Cloud backups**, not the mirror destination above. See [cloud-backup.md](cloud-backup.md). After moving PCs, reconnect providers (OAuth is machine-bound).

## Static HTML export

Read-only escape hatch under AppData `exports\` (or chosen path). Not the primary editor.
