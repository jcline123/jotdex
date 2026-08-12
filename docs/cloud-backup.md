# Cloud backup (multi-provider)

Jotdex can upload **encrypted Move Kits** on a schedule to personal **Dropbox**, **Google Drive**, and/or **OneDrive** accounts through their APIs. This is **not** the same as the filesystem **Cloud backup mirror** (Settings → Backup → Cloud backup mirror), which still copies the live vault folder one-way with robocopy.

Design notes: [decisions/0007-direct-multi-provider-cloud-backup.md](decisions/0007-direct-multi-provider-cloud-backup.md).

## What gets uploaded

| Artifact | Default | Contents |
|---|---|---|
| Encrypted Move Kit (`.jotdexkit`) | Always | Vault + app data needed for restore (same idea as Create move kit) |
| Readable vault-only ZIP | Off | Markdown notes + `.assets` only — **unencrypted** |

Readable ZIP never includes auth, history, secrets, cloud OAuth tokens, or program files. It is an emergency note-recovery fallback; full PC recovery still uses the Move Kit + `Restore-Jotdex.ps1`. Enabling it roughly doubles cloud storage per generation.

## Settings UI

**Settings → Backup → Cloud backups**

- Interval (hours) and versions to keep (per provider)
- Optional readable vault ZIP (confirm dialog on first enable)
- Per-provider cards: available in build, connection, health, Connect / Disconnect / Retry / Backup now, Move Kit + Vault ZIP status, quota when the provider reports it
- Save cloud backup / Run now

Home shows a health banner when aggregate status is not Healthy or Not configured (Open settings + Retry).

## OAuth client configuration (from Settings)

Each provider card in **Settings → Backup → Cloud backups** has:

1. A link to create the app in that provider’s developer console  
2. The loopback redirect URI to register (`http://127.0.0.1:5180/oauth/…`)  
3. A field to paste the **App key / Client ID** (optional client secret)  
4. **Save app settings** then **Connect** (opens the provider’s OAuth consent page)

Client IDs are stored in `data/config/cloud-backup.json` (portable with Move Kits). Process env vars (`JOTDEX_CLOUD_*`) still override when set — useful for packaged builds that ship with Jotdex-owned apps later.

| Provider | Console | Field |
|---|---|---|
| Dropbox | [Dropbox App Console](https://www.dropbox.com/developers/apps) | App key |
| Google Drive | [Google Cloud Credentials](https://console.cloud.google.com/apis/credentials) | Client ID |
| OneDrive | [Azure App registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade) | Application (client) ID |

**Production / portable** builds work the same way from the GUI. Optional env vars remain supported for automation.

Default loopback redirect URIs (register these exactly):

- `http://127.0.0.1:5180/oauth/dropbox`
- `http://127.0.0.1:5180/oauth/google`
- `http://127.0.0.1:5180/oauth/onedrive`

**Connect is loopback-only.** After you sign in, the provider redirects to `/oauth/…` on this PC; Jotdex completes PKCE token exchange, stores refresh tokens under `data/secrets/cloud-backup.json` (DPAPI), and Settings polls until the provider shows Connected. Tokens are never packed into Move Kits.

## Provider notes

- **Dropbox** — App Folder paths (`/Backups/<backupSetId>/…`). Simple upload for ≤8 MiB; upload sessions for larger files. Success requires matching Dropbox `content_hash`.
- **Google Drive** — `drive.file` scope; resumable uploads; MD5 verified when Drive returns `md5Checksum`; folder id reused via settings `RemoteRootId`.
- **OneDrive** — `Files.ReadWrite.AppFolder` + personal (`consumers`) tenant; Graph upload sessions; size (and `sha256Hash` when present) verified. Microsoft apps may require **preview / unverified publisher** consent for personal accounts until the Azure app is published — see ADR 0007.

## Retention and verification

Each successful generation is kept until newer complete generations exceed **Versions to keep**. Remote size/hash checks run before success is recorded. One provider failing does not block the others.

## Restore

1. Download the latest `.jotdexkit` from the provider (or use a local move kit).
2. Run `Restore-Jotdex.ps1` (enter unlock password if asked).
3. Prefer a **local-disk** vault on the new PC.
4. **Reconnect** Dropbox / Google Drive / OneDrive in Settings → Backup → Cloud backups (credentials do not travel with the kit).

Readable vault ZIPs can be unzipped anywhere to recover Markdown files without Jotdex.

## Data folders

| Path | Portable? |
|---|---|
| `data/config/cloud-backup.json` | Yes (preferences in move kits) |
| `data/secrets/cloud-backup.json` | No — reconnect after restore |
| `data/state/cloud-backup/` | No |
| `data/exports/cloud-backup-staging/` | No (transient) |

## Related

- Filesystem mirror (separate): [vault-mirror.md](vault-mirror.md), Settings → Cloud backup mirror
- Move kit / backup ZIP: [backup.md](backup.md)
- Manual test matrices: [cloud-backup-matrices.md](cloud-backup-matrices.md) (pending live verification)
