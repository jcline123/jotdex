# Architecture overview

## Runtime

```text
Browser (Win/Mac)
  → React SPA (src/Web)
  → ASP.NET Core + Kestrel (src/Server)
       → Vault service (files)
       → Index service (SQLite FTS)
       → History / trash (AppData)
       → Export / mirror jobs (later)
```

## Projects

| Project | Role |
|---|---|
| `src/Core` | Domain models, interfaces |
| `src/Infrastructure` | Filesystem, SQLite, import/migrate tools |
| `src/Server` | Host, API, auth, static SPA serving |
| `src/Web` | React + TypeScript + Vite UI |

## Data separation

- **Vault** — user content only (movable).
- **AppData** — config, auth, indexes, history, trash, logs, exports.

## Milestone focus

Ship incrementally: shell → read-only browse → search → safe edit/autosave/history → rich editor → auth/packaging → static export + iCloud mirror scripts.
