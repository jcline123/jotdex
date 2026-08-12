# Read-only vault mirror (e.g. to iCloud)

**Never** point the live Jotdex vault at an iCloud-synced folder. Use a local-disk vault, then copy a read-only snapshot elsewhere.

## Recommended flow

```text
C:\JotdexVault          (live, local disk)
        |
        |  scheduled robocopy / mirror script
        v
iCloud Drive\JotdexMirror   (browse offline; not written by the server)
```

## Script

From the repo (or copy beside your install):

```powershell
.\scripts\mirror-vault.ps1 `
  -Source "C:\JotdexVault" `
  -Destination "$env:USERPROFILE\iCloudDrive\JotdexMirror"
```

Uses `robocopy /MIR` by default (destination mirrors source). Review the destination path carefully — `/MIR` deletes files on the destination that are gone from the source.

Schedule with Task Scheduler (daily/hourly) while Jotdex is idle or running; robocopy handles locked files with retries.

## Rules

- Source = authoritative local vault only.
- Destination = read-only for humans / other devices.
- Do not run Jotdex with `VaultPath` set to the mirror.
- Auth, history, and search indexes stay in app `data\` — they are not part of this mirror.

This filesystem mirror is **independent** of multi-provider cloud backup (API uploads of encrypted Move Kits). Configure that under **Settings → Backup → Cloud backups** — see [cloud-backup.md](cloud-backup.md).
