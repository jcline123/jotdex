# Jotdex — Agent Guide

**Product:** self-hosted Markdown notebook server.  
**Key decision:** files are the product; the web app is a replaceable editor/search UI.

## Read first

1. [STATUS.md](STATUS.md) — current focus, blockers, run commands  
2. [CHECKLIST.md](CHECKLIST.md) — markable tasks with stable IDs (`S0-01`, `M1-04`, …)  
3. [docs/changelog.md](docs/changelog.md) — **why** recent fixes/behavior changes (read when debugging; update when landing non-trivial fixes)  
4. [docs/vault-format.md](docs/vault-format.md) — vault schema (do not violate)  
5. [docs/portability.md](docs/portability.md) — move vault / iCloud mirror rules  
6. [docs/brief/Windows_Markdown_Notes_Server_Agent_Brief.md](docs/brief/Windows_Markdown_Notes_Server_Agent_Brief.md) — engineering detail  

## Stack

| Layer | Choice |
|---|---|
| Backend | .NET 10, ASP.NET Core, Kestrel, self-contained win-x64 |
| Frontend | React, TypeScript, Vite |
| Editor (M5+) | Tiptap (visual-first) |
| Markdown render | Markdig |
| Search | SQLite FTS5 (word + trigram), rebuildable |
| Auth (M6+) | Cookie auth, local admin |

## Non-negotiables

- Markdown + `.assets` on disk are canonical. SQLite is disposable/rebuildable.
- No silent loss of unsupported Markdown/HTML — preserve or force source mode.
- No in-app OneNote importer — offline migration only (`OPS-*`).
- Live vault on **local disk only** — never inside iCloud while the server writes.
- iCloud = scheduled **read-only** mirror/backup only.
- No cloud telemetry. Docker not required for production.
- One writable server process per vault.
- Path containment: reject `..`, junctions, ADS escapes.
- Editing requires **autosave** + **per-note history/rollback**.
- HTML allowed only as sanitized sidecars in `.assets` (Markdown-plus-assets).
- When changing editor/Markdown formatting features, update [`src/Web/src/jotdexAiPrompt.ts`](src/Web/src/jotdexAiPrompt.ts) in the same change (Copy AI prompt must list all supported formats).
- When adding/changing app-data folders, config, or install layout, update first-run setup + move-kit + Update-Jotdex scripts/docs in the same change (see `.cursor/rules/jotdex-install-move-kit.mdc`).
- Portable upgrades: [`docs/upgrading.md`](docs/upgrading.md) — Settings → Updates + `Update-Jotdex.ps1`.

## Agent protocol

1. Read `STATUS.md`, `docs/changelog.md` (for prior fix context), and the next unchecked items for the **active milestone only**.
2. Claim work in `STATUS.md` → In progress.
3. Complete the smallest coherent checklist item; mark `- [x]` in `CHECKLIST.md`.
4. Do not start a later milestone until the previous **gate** is done (or Joshua overrides).
5. Stop and report if a change would violate a non-negotiable.
6. Do not commit secrets or personal vault content into git.
7. After a non-trivial fix or behavior change: append a short *why* entry to `docs/changelog.md` (and prefer commit messages that match). ADRs in `docs/decisions/` for larger design choices.

### Autonomous verification

Joshua wants agents to **start the server and run checks themselves** — do not hand him start commands as the default. After meaningful changes: rebuild SPA if needed, run tests, ensure `http://127.0.0.1:5180` is up (Development + SampleVault), probe `/api/health` and feature APIs, and fix connection/port issues before asking him to look.

## Milestone order

`S0` → `M0` → `M1` (gate) → `M2` → `M3` → `M4` → `M5` → `M6` → `M7` → `OPS` (when export path provided)

## Repo layout

```text
src/Server | Core | Infrastructure | Web
tests/Unit | Integration | RoundTripFixtures | E2E
tools/SampleVault | MigrateExport | TestVaultGenerator
scripts/
docs/
```
