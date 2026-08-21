# Jotdex STATUS

**Active milestone:** CB — Multi-provider cloud backup (code complete; OneDrive live-verified)  
**Last updated:** 2026-08-21

## In progress

- Dropbox / Google personal-account matrices (`CB-24` / `CB-34`) still pending. OneDrive live upload verified (`CB-44` notes in matrices when filled).

## Just shipped (this session)

- Home + expanded-rail **New note** use the same folder-picker modal as collapsed **Add note**
- Folder picker modal for Move (notes + folders); drag notes/folders onto the folders rail
- Version **1.1.18**

## Highlights (v1.1.18)

- **New note** (Home and expanded notes rail) opens the title + folder modal, same as collapsed **Add note**

## Highlights (v1.1.17)

- Move note/folder uses a folder-tree picker (nested folders visible); drag notes or folders onto the folders rail

## Highlights (v1.1.16)

- Code-box selection copy/paste is plain text (no Markdown fences or Chrome `StartFragment`/`span` junk)
- Formatting bar: strikethrough and Clear formatting

## Highlights (v1.1.15)

- Backup settings: filesystem copy renamed **Vault mirror** (local / USB / UNC) so it is distinct from API **Cloud backups**

## Highlights (v1.1.14)

- Multi-provider cloud backup (API uploads) separate from filesystem vault mirror; optional readable vault ZIP
- Configure providers from Settings (no env required for personal OAuth apps)

## Highlights (v1.1.13)

- Idle lock no longer fires early from a short session cookie while you are still clicking/typing; cookie stays long, idle timer owns the walk-away lock

## Highlights (v1.1.12)

- Edited notes bump `modified` and rise to the top of the folder notes list
- Formatting bar collapse uses wider hysteresis + cooldown so it does not flash while scrolling

## Highlights (v1.1.11)

- Todos rail ignores idle-lock 401s and reloads after unlock (no leftover “Could not save/update” error)

## Highlights (v1.1.10)

- README sells the product first, then install (portable zip is the fastest path)
- Cloudflare Tunnel documented next to VPN for remote access (tunnel to loopback; don’t port-forward 5180)

## Highlights (v1.1.9)

- Link color in notes readable on dark theme (`--link`)
- Consistent line spacing: Shift+Enter = paragraph outside lists/tables; smart paste converts `<br>` to paragraphs

## Highlights (v1.1.8)

- Idle lock, clip default folder, and Home recently-viewed stored on the server (same on every device)
- Unlock after idle returns to Home instead of a failed-save banner on the open note

## Highlights (v1.1.7)

- Toolbar formatting only affects selected text (trims invisible selection overhang into neighbouring blocks)
- Heading on a full line toggles the block instead of splitting (no leftover empty paragraphs)

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
- Cloud backup Dropbox/Google live matrices pending (`docs/cloud-backup-matrices.md`)

## Run (Development + SampleVault)

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
cd src\Server
dotnet run --no-launch-profile
```

Listen URL comes from `data/config/network.json` when present (often `http://127.0.0.1:5180`).

Optional provider env vars still override Settings-pasted client IDs: `JOTDEX_CLOUD_DROPBOX_APP_KEY`, `JOTDEX_CLOUD_GOOGLE_CLIENT_ID`, `JOTDEX_CLOUD_ONEDRIVE_CLIENT_ID`.
