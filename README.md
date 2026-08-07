# Jotdex

Self-hosted Markdown notebook server. **Files are the product** — the web app is a replaceable UI over a portable Markdown vault.

See [AGENTS.md](AGENTS.md), [STATUS.md](STATUS.md), [CHECKLIST.md](CHECKLIST.md), and the full install guide **[SETUP.md](SETUP.md)**.

## Quick start (dev)

```powershell
cd src\Web
npm install
npm run build
cd ..\Server
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls http://127.0.0.1:5180 --no-launch-profile
```

Open http://127.0.0.1:5180 — big search bar at the top (Ctrl+K). Point **Vault** at your Markdown folder anytime.

## Publish

```powershell
.\scripts\publish-win-x64.ps1
```

Output: `artifacts\win-x64\`

## Important

- Live vault on **local disk only** (e.g. `C:\JotdexVault`), never inside iCloud sync.
- Use **Settings → Cloud backup mirror** for one-way copy to iCloud/OneDrive/etc.

## OneNote → Markdown

Export with [alxnbl/onenote-md-exporter](https://github.com/alxnbl/onenote-md-exporter), then migrate offline:

```powershell
.\tools\MigrateExport\Migrate-OneNoteMdExporter.ps1 `
  -SourceRoot "…\Exports\md" `
  -Destination "C:\JotdexVault"
```

Details: [docs/import-format/onenote-md-exporter.md](docs/import-format/onenote-md-exporter.md).
