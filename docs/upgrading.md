# Updating Jotdex

Keep your **vault** and install **`data\`** folder. Replace only the program files.

## From Settings (recommended)

1. Open **Settings → Updates → Check for updates**.
2. If a newer GitHub Release exists with a portable zip, follow the on-screen steps.
3. In File Explorer, open the folder that contains `Jotdex.Server.exe`.
4. Run **`Update-Jotdex.ps1`** (Run with PowerShell).

The script will:

1. Stop Jotdex
2. Back up the current program to `C:\JotdexBackupHold\jotdex-prog-…` (not vault / not `data\`)
3. Download the latest Release zip
4. Replace program files
5. Start Jotdex and wait for health
6. Ask you to confirm everything looks OK (window stays open)
7. If you say no (or health fails), restore that backup and restart

## Publishing a release (maintainers)

```powershell
.\scripts\publish-win-x64.ps1
```

Upload **`artifacts\jotdex-win-x64.zip`** to a GitHub Release (tag like `v1.1.0`). Asset name should include `win-x64` or `portable` so the checker finds it.

## Manual update

1. Settings → Backup → Create move kit (optional safety net) or Create backup ZIP.
2. Stop Jotdex.
3. Download the Release zip, extract over the install folder, **keep `data\`**.
4. Start `start-portable.cmd`.
