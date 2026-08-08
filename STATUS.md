# Jotdex STATUS

**Active milestone:** V1 complete / polish  
**Last updated:** 2026-08-08

## Highlights

- Live vault on local disk; optional one-way cloud mirror (Settings)
- Restart server button for bind/port/HTTPS
- Code boxes with language + Copy in the editor
- Auth, search, TipTap editor, static export, backup ZIP, integrity scan
- **Share HTML** on a note (self-contained); full-vault HTML export under Settings
- **Home landing** in the note pane on unlock (recents, new notes, todos) — click brand to return
- **Todos** rail/tab backed by vault-root `Todos.md` (done items disappear with 30s undo; browser reminders while tab is open)
- Desktop: collapsible Folders / Notes / Todos columns for more editor space

## Durable memory

- Fix / behavior rationale: [`docs/changelog.md`](docs/changelog.md) — agents must read when relevant and append when landing non-trivial changes
- Architecture decisions: [`docs/decisions/`](decisions/)

## Known small polish (not blocking)

- Search box can show `undefined` as the value
- Leftover `tools/SampleVault/Conflict *.md` from earlier conflict tests (safe to delete)
- NuGet NU1903 warning on SQLitePCLRaw (dependency bump later)

## Persistence after reboot

- **Settings → Start with Windows** — creates a Startup-folder shortcut for the current user (no Admin needed).
- **Machine-wide service:** run `artifacts\win-x64\install-service.ps1` elevated once (Automatic start).
- **Logs:** `{app data}\logs\jotdex-YYYYMMDD.log` — also **Settings → Logs**.

## Dev run

```powershell
cd src\Web; npm install; npm run build
cd ..\Server
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --no-launch-profile
```

Open http://127.0.0.1:5180 (or the listen URL from Settings → Network). Prefer saved `network.json` over launchSettings; LAN binds `0.0.0.0` when enabled.
