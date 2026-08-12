# Vault mirror (filesystem copy)

**Never** point the live Jotdex vault at a cloud-synced folder. Use a local-disk vault, then optionally mirror a one-way copy elsewhere (another local path, USB, UNC share, or a sync-client folder for offline browsing).

## Recommended flow

```text
C:\JotdexVault          (live, local disk)
        |
        |  scheduled robocopy / in-app mirror
        v
D:\JotdexMirror   or   \\server\share\JotdexMirror
        (browse / recover; not written by the server as the live vault)
```

In the app: **Settings → Backup → Vault mirror**.

## Script

From the repo (or copy beside your install):

```powershell
.\scripts\mirror-vault.ps1 `
  -Source "C:\JotdexVault" `
  -Destination "D:\JotdexMirror"
```

Uses `robocopy /MIR` by default (destination mirrors source). Review the destination path carefully — `/MIR` deletes files on the destination that are gone from the source.

Schedule with Task Scheduler (daily/hourly) while Jotdex is idle or running; robocopy handles locked files with retries.

## Rules

- Source = authoritative local vault only.
- Destination = read-only for humans / other devices.
- Do not run Jotdex with `VaultPath` set to the mirror.
- Auth, history, and search indexes stay in app `data\` — they are not part of this mirror.

This filesystem mirror is **independent** of multi-provider cloud backup (API uploads of encrypted Move Kits). Configure that under **Settings → Backup → Cloud backups** — see [cloud-backup.md](cloud-backup.md).
