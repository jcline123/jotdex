# Updating Jotdex

Keep your **vault** and install **`data\`** folder. Replace only the program files.

## From Settings (recommended)

1. Open **Settings → Updates → Check for updates**.
2. If a newer GitHub Release exists with a portable zip, follow the on-screen steps.
3. In File Explorer, open the folder that contains `Jotdex.Server.exe`.
4. Run **`Update-Jotdex.ps1`** (Run with PowerShell).

The script will:

1. Stop Jotdex
2. Back up the current program to `C:\JotdexBackupHold\jotdex-prog-…` (not vault / not `data\`)
3. Download the latest Release zip
4. Replace program files
5. Start Jotdex and wait for health
6. Ask you to confirm everything looks OK (window stays open)
7. If you say no (or health fails), restore that backup and restart

## Publishing a release (maintainers)

```powershell
.\scripts\publish-win-x64.ps1
```

Upload **`artifacts\jotdex-win-x64.zip`** to a GitHub Release (tag like `v1.1.0`). Asset name should include `win-x64` or `portable` so the checker finds it.

## Manual update

1. Settings → Backup → Create move kit (optional safety net) or Create backup ZIP.
2. Stop Jotdex.
3. Download the Release zip, extract over the install folder, **keep `data\`**.
4. Start `start-portable.cmd`.

## 1.3.0 (editor UX)

1.3.0 adds slash `/`, gutter `+` (on the 1.2.2 gap cursor), bubble formatting, block move, table chrome, image inspector/figures, link popover and bookmark cards, Details, highlight/underline/sub/sup, alignment comments, titled/collapsible callouts, live outline, and bundled KaTeX. Snipping Tool / clipboard pictures show after paste. Long notes with HTML in code fences open in **Visual**; switch to Source yourself if you need it (`script` / `iframe` / `javascript:` still force Source). On-disk notes stay ordinary Markdown (dialect v2). Rollback is the previous portable exe (1.2.2). 1.2.2 Source can still read the new Markdown; visual dialect features need 1.3.0.

## 1.2.2 (caret between stacked blocks)

1.2.2 is a small editor follow-up. Two code boxes (or a picture against a code box) with no line between them now take a blinking accent caret in the seam — Arrow Down from the end of the first box, or a click in that gap, then Enter or type. The boxes themselves look the same. Rollback is the previous portable exe (1.2.1).

## 1.2.1 (Share/export callouts)

1.2.1 is a small follow-up to 1.2.0. Callouts you save as `> [!warning]` (or `> [!tip] Title`) now keep their color in **Share HTML** and static vault export. The editor and ranking notes are unchanged. Rollback is the previous portable exe (1.2.0).

## 1.2.0 (official Markdown engine)

1.2.0 replaces the community Markdown bridge with official Tiptap Markdown. Your notes stay ordinary `.md` files. The portable exe is still self-contained — **you do not install Node.js** on the PC that runs Jotdex.

Before switching the running program:

1. Keep a full vault backup (move kit, backup ZIP, or a filesystem copy). Opening notes without editing should not rewrite them; a backup is still the rollback for disk accidents.
2. After the new exe is running, spot-check a few notes: a callout, a colored span, a task with `<!-- jotdex-task -->`, an image followed by a heading, and `Todos.md`.
3. Some notes (especially a picture mixed into the same paragraph as text, leftover OneNote HTML, or `javascript:` links) open in **Source** on purpose so Jotdex does not flatten them. That is not data loss.

A vault **audit** (`npm run markdown:migrate`) exists for developers on a machine that already has this repo and Node. It is optional, read-only by default, and must target a *copy* of the vault — never the only live folder. Production installs do not need it.

Rollback of 1.2.0 is the previous portable exe (1.1.24) plus your vault backup. Do not run a bulk “save every note” after upgrade.
