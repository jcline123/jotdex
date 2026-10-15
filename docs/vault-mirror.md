# Vault mirror (zip archive)

**Never** point the live Jotdex vault at a cloud-synced folder. Use a local-disk vault, then optionally mirror a one-way **zip** elsewhere (another local path, USB, UNC share, or a sync-client folder such as iCloud Drive).

## Why a zip?

Windows sync clients (especially iCloud) are unreliable with thousands of small Markdown and `.assets` files. Jotdex writes a single archive:

```text
C:\JotdexVault                 (live, local disk)
        |
        |  scheduled / in-app mirror
        v
…\JotdexMirror\
    jotdex-vault-mirror.zip    (one file for sync)
    README-JOTDEX-MIRROR.txt
    jotdex-move-kits\          (optional daily recovery kits)
```

The live vault is **never** modified by the mirror. After each successful zip, leftover file-tree copies from older Jotdex versions are removed from the destination (the zip and `jotdex-move-kits\` are kept).

## In the app

**Settings → Backup → Vault mirror** — enable, set destination folder, interval, optional daily move kit, **Mirror now**.

## Script

```powershell
.\scripts\mirror-vault.ps1 `
  -Source "C:\JotdexVault" `
  -Destination "C:\Users\you\iCloudDrive\JotdexMirror"
```

## Rules

- Source = authoritative local vault only.
- Destination = read-only for humans / other devices (browse by unzipping elsewhere).
- Do not run Jotdex with `VaultPath` set to the mirror folder or an unzip of the archive.
- Auth, history, and search indexes stay in app `data\` — they are not part of this mirror.

This filesystem mirror is **independent** of multi-provider cloud backup (API uploads of encrypted Move Kits). Configure that under **Settings → Backup → Cloud backups** — see [cloud-backup.md](cloud-backup.md).
