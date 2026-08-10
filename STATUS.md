# Jotdex STATUS

**Active milestone:** Post-1.1.6 polish  
**Last updated:** 2026-08-10

## In progress

- (none)

## Highlights (v1.1.6)

- Idle lock always shows the lock screen (no half-open UI after timeout / 401)

## Highlights (v1.1.5)

- Collapsed folders/notes rails: selected folder label, Add note with folder picker (rail stays collapsed)
- Click note title in the editor header to rename inline

## Highlights (v1.1.4)

- **Clip page** fetches title/summary into a new or open note (server-side; curl on Windows for Cloudflare)
- Bookmarklet → Save web clip modal (no separate Capture screen); Settings → Capture for install
- Lock screen copy tightened

## Highlights (v1.1.2)

- Trash browser (notes header; not a permanent mobile tab)
- Notes newest-first + favorites; hide standalone `Todos.md` from notes/home lists
- Todos rail: From notes tasks editable (priority/due/remind); refreshes after trash
- `/capture` + clip API; history Compare; attachment text in FTS
- Dev Start with Windows uses `dotnet run` so SPA loads after reboot

## Durable memory

- Fix / behavior rationale: [`docs/changelog.md`](docs/changelog.md)
- Architecture decisions: [`docs/decisions/`](decisions/)

## Known small polish (not blocking)

- Search box can show `undefined` as the value
- Leftover `tools/SampleVault/Conflict *.md` from earlier conflict tests (safe to delete)
- NuGet NU1903 warning on SQLitePCLRaw (dependency bump later)
- PDF attachment text / OCR still deferred; bookmarklet needs same-origin auth cookie

## Persistence after reboot (this Dev PC)

- Startup: `%APPDATA%\...\Startup\Jotdex Server.cmd` → `dotnet run --no-launch-profile` in `src\Server`
- Portable users: Start with Windows from the portable exe (unchanged)

## Run (Development)

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
cd src\Server
dotnet run --no-launch-profile
```

Listen URL from `data/config/network.json` when present (often `http://0.0.0.0:5180`).
