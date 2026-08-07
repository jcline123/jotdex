# Jotdex STATUS

**Active milestone:** V1 complete / polish  
**Last updated:** 2026-08-07

## Highlights

- Live vault on local disk; optional one-way cloud mirror (Settings)
- Restart server button for bind/port/HTTPS
- Code boxes with language + Copy in the editor
- Auth, search, TipTap editor, static export, backup ZIP, integrity scan

## Dev run

```powershell
cd src\Web; npm install; npm run build
cd ..\Server
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --no-launch-profile
```

Open http://127.0.0.1:5180 (or the listen URL from Settings → Network).
