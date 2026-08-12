# Backup bundles

Jotdex keeps **notes on disk in the vault**. App data is separate.

## Encrypted move kit (.jotdexkit)

When a Jotdex unlock password is set, Create move kit writes an AES-encrypted **`.jotdexkit`** (safer in cloud folders). Restore stays one step:

1. Put the kit next to `Restore-Jotdex.ps1` and `Jotdex.Server.exe` (your portable folder is fine).
2. Run **Restore-Jotdex.ps1** (right-click → Run with PowerShell).
3. Enter your unlock password when asked — Restore decrypts and continues.
4. Choose install + vault folders.

No separate decrypt command required.

### Daily kit in the cloud mirror

Mirror still copies your **whole vault** as normal. Optionally also enable **Also drop a daily recovery move kit** — that adds `jotdex-move-kits\` inside the same mirror destination (one archive + Restore script) for PC-loss recovery.

---

## Easiest: move to another PC (recommended)

**Settings → Backup → Move to another PC → Create move kit (ZIP)** builds:

`data\exports\backups\jotdex-move-YYYYMMDD-HHMMSS.zip`

| Entry | Purpose |
|---|---|
| `vault/` | Full live vault |
| `appdata/config/` | Vault path, network, mirror settings, **cloud-backup.json** preferences (portable) |
| `appdata/auth/` | Password hash (secret) |
| `appdata/history/` | Note rollback snapshots |
| `appdata/secrets/secrets-portable.json` | Unwrapped notification / TOTP secrets for transfer (re-wrapped with DPAPI on first start) |
| `app/` | Portable `Jotdex.Server.exe` when the kit was created from the portable build |
| `Restore-Jotdex.ps1` | Guided restore on the new PC |
| `README-MOVE.txt` | Plain-English steps |
| `MANIFEST.json` | Timestamp + flags |

Indexes are **never** included (rebuilt on first start).

**Not in move kits / backup ZIPs**

| Path | Why |
|---|---|
| `data/secrets/cloud-backup.json` | DPAPI OAuth tokens — machine-bound; reconnect providers after restore |
| `data/state/cloud-backup/` | Runtime status |
| `data/exports/cloud-backup-staging/` | Transient staging for cloud backup generations |

Encrypted kits (`.jotdexkit`) use **JDXK2** (streaming) when created with a password; `Decrypt-JotdexKit.ps1` / `Jotdex.Server.exe --decrypt-kit` also still decrypt legacy **JDXK1**. Restore decrypts for you.

### On the new PC

1. Copy the ZIP and unzip it.
2. Run `Restore-Jotdex.ps1` (Run with PowerShell, or `powershell -NoProfile -ExecutionPolicy Bypass -File .\Restore-Jotdex.ps1`).
3. Choose an **install folder** (e.g. `C:\Jotdex`) and a **local-disk vault** folder (e.g. `C:\JotdexVault` — not iCloud).
4. If the kit had **LAN** enabled, Restore prompts UAC to open Windows Firewall for HTTP/HTTPS (cancel is OK — open ports manually later).
5. Start `start-portable.cmd` → open http://127.0.0.1:5180 → unlock with your existing password.

If you created the kit while running `dotnet run` (dev), the ZIP may omit `app\`. Use the portable build’s Settings button, or from the repo:

```powershell
.\scripts\create-move-kit.ps1 -VaultPath C:\JotdexVault
```

Treat move-kit ZIPs that include `appdata\auth` or `appdata\secrets\secrets-portable.json` as secrets.

## Multi-provider cloud backup (API upload)

**Settings → Backup → Cloud backups** schedules encrypted Move Kits (optional readable vault-only ZIP) to Dropbox / Google Drive / OneDrive via their APIs. Separate from the filesystem mirror below. User guide: [cloud-backup.md](cloud-backup.md). After restore, reconnect providers — OAuth tokens are not in the kit.

---

## Data-only backup ZIP

**Settings → Backup → Create backup ZIP** (or `POST /api/admin/backup`) writes:

`data\exports\backups\jotdex-backup-YYYYMMDD-HHMMSS.zip`

Same vault + appdata as above, **without** the program or restore script. Query flags: `?includeAuth=false` / `?includeHistory=false`.

## Manual minimum backup

1. Stop Jotdex (or the Windows Service).
2. Copy the entire **vault** folder (all `.md` and `.assets`).
3. Optionally copy `data\config\`, `data\auth\`, `data\history\`.
4. Resume Jotdex.

## Locations

| Mode | App data root |
|---|---|
| Portable | `.\data` beside `Jotdex.Server.exe` |
| Service / normal | `%LOCALAPPDATA%\Jotdex` |

## Static HTML export

**Settings / note → Share HTML** or full-vault static export under Maintenance. Read-only escape hatch — not the primary editor.
