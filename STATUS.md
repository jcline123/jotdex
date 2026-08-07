# Jotdex STATUS

**Active milestone:** V1 complete / polish  
**Last updated:** 2026-08-07

## Highlights

- Live vault on local disk; optional one-way cloud mirror (Settings)
- Restart server button for bind/port/HTTPS
- Code boxes with language + Copy in the editor
- Auth, search, TipTap editor, static export, backup ZIP, integrity scan
- **Share HTML** on a note (self-contained); full-vault HTML export under Settings

## Durable memory

- Fix / behavior rationale: [`docs/changelog.md`](docs/changelog.md) — agents must read when relevant and append when landing non-trivial changes
- Architecture decisions: [`docs/decisions/`](docs/decisions/)

## Known small polish (not blocking)

- Search box can show `undefined` as the value
- Leftover `tools/SampleVault/Conflict *.md` from earlier conflict tests (safe to delete)
- NuGet NU1903 warning on SQLitePCLRaw (dependency bump later)

## Dev run

```powershell
cd src\Web; npm install; npm run build
cd ..\Server
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --no-launch-profile
```

Open http://127.0.0.1:5180 (or the listen URL from Settings → Network). Prefer saved `network.json` over launchSettings; LAN binds `0.0.0.0` when enabled.
