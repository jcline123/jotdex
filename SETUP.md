# Jotdex setup guide

How to install or move Jotdex onto a Windows machine. Keep the **vault** (your Markdown files) separate from the app.

## What you need

- Windows 10/11 x64
- This repository (or a published `win-x64` build)
- For development builds: [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Node.js LTS](https://nodejs.org/)
- For published builds: **no** Node or .NET install required on the target PC

## Important: vault vs app data

| Thing | What it is | Where |
|---|---|---|
| **Vault** | Your notes: folders, `.md`, `.assets` | Local disk, e.g. `C:\JotdexVault` |
| **App data** | Search index, history, auth, config | `%LOCALAPPDATA%\Jotdex` or `.\data` beside the exe |

**Do not** put the live vault inside iCloud Drive. Use **Settings → Cloud backup mirror** for a one-way copy to iCloud/OneDrive/etc. Live writes + sync = conflict risk.

Moving PCs: copy the vault folder, install Jotdex, point Settings → Vault at the new path, Rescan.

## Option A — Development run (this machine)

```powershell
git clone https://github.com/jcline123/jotdex.git
cd jotdex\src\Web
npm install
npm run build
cd ..\Server
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls http://127.0.0.1:5180 --no-launch-profile
```

Open http://127.0.0.1:5180

- Dev defaults to `tools\SampleVault` until you change it.
- In the app: **Vault** → browse or paste a path like `C:\JotdexVault` → **Use this folder**.

## Option B — Published portable build

```powershell
cd jotdex
.\scripts\publish-win-x64.ps1
```

Copy `artifacts\win-x64\` to the server PC (or zip it). Run:

```powershell
cd artifacts\win-x64
.\start-portable.cmd
```

Open http://127.0.0.1:5180 and complete **first-run setup** (vault folder, admin account, bind/port).

See [docs/packaging.md](docs/packaging.md) for Windows Service install and upgrades.

## First-time checklist

1. Create an empty folder on local disk: `C:\JotdexVault` (or copy an existing Markdown tree there).
2. Start Jotdex (portable or Development).
3. **Production / portable:** first-run wizard → vault → admin → network.
4. **Development:** SampleVault is preloaded; use **Settings** to point at your vault.
5. Confirm notes appear in the left tree; try search (Ctrl+K).
6. Optional: Windows Service via `install-service.ps1` (elevated). See packaging docs.

## Bind address

Default is this PC only (`127.0.0.1`). Change in **Settings → Network** (restart to apply), or:

```powershell
.\Jotdex.Server.exe --urls http://0.0.0.0:5180
```

Do not expose the port to the public Internet without a reverse proxy / VPN.

## OneNote import (offline)

1. Export notebooks with [alxnbl/onenote-md-exporter](https://github.com/alxnbl/onenote-md-exporter) → `Exports\md\…`.
2. Migrate into a **local-disk** vault (source export is never modified):

```powershell
.\tools\MigrateExport\Migrate-OneNoteMdExporter.ps1 `
  -SourceRoot "C:\Users\YOU\Downloads\OneNoteMdExporter…\Exports\md" `
  -Destination "C:\JotdexVault"
```

3. In Jotdex **Settings**, set vault path to `C:\JotdexVault` → Rescan.

Full notes: [docs/import-format/onenote-md-exporter.md](docs/import-format/onenote-md-exporter.md).

## Reinstall / new PC

1. Copy vault folder to the new PC.
2. Install/publish Jotdex (Option A or B).
3. Start app → set vault path → Rescan.
4. Search index rebuilds automatically. History lives in app data (optional to copy).

## Useful URLs

| URL | Purpose |
|---|---|
| `/` | App UI |
| `/api/health` | Status + search capability |
| `/api/settings/vault` | Current vault path |
| `/api/search?q=...` | Search API |

## Docs in this repo

- [AGENTS.md](AGENTS.md) — agent/dev rules
- [docs/vault-format.md](docs/vault-format.md) — how notes are stored
- [docs/import-format/onenote-md-exporter.md](docs/import-format/onenote-md-exporter.md) — OneNote → vault
- [docs/portability.md](docs/portability.md) — moving vaults / iCloud mirror
- [docs/search.md](docs/search.md) — search behavior
- [STATUS.md](STATUS.md) — current project status
