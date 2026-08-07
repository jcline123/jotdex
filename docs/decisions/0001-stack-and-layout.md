# ADR 0001: Stack and repository layout

## Status

Accepted

## Context

Need a Windows-native self-hosted notes server with browser UI, self-contained publish, and no IIS/Docker requirement for production.

## Decision

- Backend: .NET 10 LTS, ASP.NET Core, Kestrel
- Frontend: React + TypeScript + Vite, served as static assets from ASP.NET Core
- Layout: `src/Server`, `src/Core`, `src/Infrastructure`, `src/Web` plus `tests/`, `tools/`, `scripts/`, `docs/`

## Consequences

Single publish produces win-x64 self-contained output. Frontend build is a publish step dependency.
