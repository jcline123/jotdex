# Jotdex

**A private notebook on your Windows PC. The files are yours.**

[![Latest release](https://img.shields.io/github/v/release/jcline123/jotdex?label=latest)](https://github.com/jcline123/jotdex/releases/latest)

Jotdex is a self-hosted notes app that runs on a Windows computer you already own. Your notes are ordinary **Markdown files in a folder on disk** — not a proprietary database, not a cloud account, not a subscription. Open them in a browser on this PC, on a Mac or phone on your network, or from elsewhere over a VPN or a [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/).

If Jotdex disappeared tomorrow, you would still have every note, picture, and attachment. Copy the folder. Open it in another editor. Zip it. Put it on a USB drive. That is the whole point.

| Piece | What it is |
|---|---|
| **Vault** | A folder of your notes (`.md` files + pictures) |
| **Jotdex** | A small program on Windows that shows and edits that folder |
| **Browser** | Where you read and write (Edge, Chrome, Safari, Firefox) |

No Docker. No Linux box. No Node or .NET runtime on the PC that *runs* it. Unzip, start, open a URL.

---

## Why people use it

**You are done renting your notes.** OneNote, Notion, and Evernote are great until the account, the sync, the export, or the product changes. Jotdex never holds your content hostage: the vault is just files.

**One Windows box, every device.** Leave it running on the PC under the desk. Open the same vault from your Mac, a laptop on the LAN, or your phone — at home on the network, or away via a VPN or Cloudflare Tunnel. No app to install on those machines. Walk away and idle lock + a password (optional TOTP) cover the screen.

**Built for notes you actually work from.** Runbooks, ticket write-ups, meeting lists, KB articles, PowerShell snippets, screenshots. Search finds `192.168.1.50` and `Get-Service`, not just whole words. Paste from a wiki or a ticket and keep the structure. Clip a webpage into a note with title and summary, not a raw URL.

**Leaving OneNote without starting over.** Export, migrate into a new vault (your original export is never touched), keep writing in a visual editor that still feels like notebooks and pages.

---

## What makes Jotdex different

Most notes apps pick two of: *nice editor*, *files you own*, *open it from any device*. Jotdex is the combination that is usually missing — a real editor, on your hardware, with the vault as normal files.

| | Jotdex | Typical cloud notes | Typical local Markdown app |
|---|---|---|---|
| Notes are ordinary files on disk | Yes | No (database / account) | Yes |
| Visual editor in the browser | Yes | Yes | Usually a desktop app per device |
| No account, no telemetry, no subscription | Yes | Rarely | Often |
| Search that finds IPs, errors, cmdlets | Built in | Weak | Plugins / maybe |
| Docker / Linux required | No | Sometimes | No |
| Live vault inside iCloud/OneDrive | **Never** (that's how files get corrupted) | Sync is the product | People often do this and regret it |

Three rules the app will not break:

1. **Files are the product.** SQLite is only a rebuildable search index and app state. Delete it; your notes are still there.
2. **Nothing is silently dropped.** The visual editor uses official Tiptap Markdown plus a Jotdex dialect. If a note cannot be represented (mixed image+text in one paragraph, some raw HTML), it opens in **Source** instead of throwing content away.
3. **The live vault stays on local disk.** iCloud and OneDrive are a **one-way backup mirror**, not the folder Jotdex writes to.

---

## Features

### Write like a notebook, store like a developer
Visual editor for headings, lists, tables, callouts, tasks, code boxes, images, and colors — saved as Markdown you can open anywhere. Drag in screenshots. Paste rich HTML, plain text, or as a code box. Wikilinks (`[[`) to other notes.

### Search that is fast on a real vault
SQLite FTS5 with a word index **and** a trigram index. Find a hostname, an error code, or half a PowerShell command across hundreds of notes. Attachment text (logs, CSV, HTML sidecars) is indexed too. The index rebuilds from the vault if it ever gets lost.

### Capture from the web
**Clip page** fetches a URL on the server and drops in title, description, and an excerpt. A bookmarklet saves the page you are looking at. Home shows recently viewed, created, and updated notes — the same on every device.

### Todos where you wrote them
Checkboxes in notes show up in a **Todos** rail with priority, due date, and reminders. Finish the task in the note it belongs to. A standalone inbox lives in `Todos.md` if you want a scratch list.

### Safety net, not a leap of faith
Every edit **autosaves**. Per-note **history** lives outside the vault so you can compare and roll back a bad change. Trash (restore or delete). Optional password, idle lock, and TOTP. **Share HTML** downloads one note as a clean single file you can email — no Jotdex branding, recipient does not need the app.

### Your network, your backup, your next PC
LAN access from Settings (firewall helper included). Away from home: a VPN, or a **Cloudflare Tunnel** so you do not open inbound ports. Optional Windows Service so it comes back after reboot. Optional **vault mirror** to another local path, UNC share, or sync folder. Optional **multi-provider cloud backup** (encrypted Move Kits to Dropbox / Google Drive / OneDrive APIs). **Move kit** ZIP to pick up the whole install on a new machine. In-app **Updates** pull the latest portable release.

### OneNote → Jotdex without touching the export
Offline migration copies into a staging vault. Your original OneNote Markdown export stays exactly as the exporter wrote it.

---

## Who it is for

- You want **OneNote-like folders and pages** but files you can backup, grep, and keep forever
- You take **IT / work notes** (runbooks, tickets, KB clips, commands) and need search that is not fuzzy mush
- You have a **Windows PC that can stay on**, and you want to open notes from a Mac or phone without installing another client (LAN, VPN, or Cloudflare Tunnel)
- You care about **privacy by architecture**: nothing leaves the machine unless you set up a backup mirror yourself

**Not the right fit if** you need several people editing the same note at once, a phone App Store client, or a hosted SaaS with no PC of your own. Jotdex is one writable server per vault — on purpose.

---

## Before you start

1. A **Windows 10 or 11** PC (x64).
2. A folder for notes, for example `C:\JotdexVault` — create it empty in File Explorer if you are starting fresh.
3. Put that folder on **local disk** (`C:\…`). **Do not** put the live vault inside a sync client folder. Use **Settings → Backup → Vault mirror** later if you want a one-way copy elsewhere, and/or **Cloud backups** for API uploads.

---

## Get Jotdex

### Option 1 — Portable zip (fastest)

No Git, no Node, no .NET install. This is the build from [GitHub Releases](https://github.com/jcline123/jotdex/releases/latest).

1. Download **`jotdex-win-x64.zip`** from the [latest release](https://github.com/jcline123/jotdex/releases/latest).
2. Unzip somewhere stable, e.g. `C:\Jotdex` (not inside the vault folder).
3. Double-click **`start-portable.cmd`**.
4. Open [http://127.0.0.1:5180](http://127.0.0.1:5180) and finish first-run (vault folder, optional admin password, network).

Later: **Settings → Updates**, or run `Update-Jotdex.ps1` from that folder. Details: [docs/upgrading.md](docs/upgrading.md).

---

### Option 2 — Guided setup from source

One script walks you through tools, vault folder, build, and first launch.  
It **asks before installing anything**. Optional installs use **winget** (Windows Package Manager) — not random downloads. It does **not** change your antivirus or global PowerShell policy.

**You already have Git:**

```powershell
cd $HOME\Downloads
git clone https://github.com/jcline123/jotdex.git
cd jotdex
.\Setup.cmd
```

**No Git yet:**

1. Download the repo ZIP from GitHub: [jcline123/jotdex](https://github.com/jcline123/jotdex) → **Code → Download ZIP**.
2. Extract it (e.g. to Downloads).
3. Double-click **`Setup.cmd`** inside the extracted folder  
   (or in PowerShell: `cd` into that folder and run `.\Setup.cmd`).

The wizard will:

1. Check for **Git**, **.NET 10 SDK**, and **Node.js LTS**
2. Offer to install anything missing via **winget** (you confirm each one)
3. Ask where your **vault** should live (default `C:\JotdexVault`)
4. Build the portable app under `artifacts\win-x64\`
5. Optionally start Jotdex and add a **Startup** shortcut

Then open [http://127.0.0.1:5180](http://127.0.0.1:5180) and finish any remaining first-run screens (admin password if asked).

**Advanced / silent-ish flags** (PowerShell):

```powershell
.\scripts\Setup-Jotdex.ps1 -VaultPath "C:\JotdexVault" -Start -AddStartupShortcut
```

---

### Option 3 — Manual setup (same result, step by step)

Use this if you prefer clicking through installs yourself, or if the guided script cannot use winget on your PC.

#### Step 1 — Get the code

1. Install [Git for Windows](https://git-scm.com/download/win) if you don’t have it.
2. Open **PowerShell**.
3. Run:

```powershell
cd $HOME\Downloads
git clone https://github.com/jcline123/jotdex.git
cd jotdex
```

#### Step 2 — Install build tools (one time)

You need these only on the PC that **builds** Jotdex (not necessarily every PC that runs it later):

1. [.NET 10 SDK](https://dotnet.microsoft.com/download) — install it, then close and reopen PowerShell.
2. [Node.js LTS](https://nodejs.org/) — install it, then close and reopen PowerShell.

Check they worked:

```powershell
dotnet --version
node --version
```

Both should print a version number (not an error).

#### Step 3 — Build the portable app

Still in the `jotdex` folder:

```powershell
.\scripts\publish-win-x64.ps1
```

Wait until it finishes. Your ready-to-run app is here:

```text
jotdex\artifacts\win-x64\
```

#### Step 4 — Start Jotdex

```powershell
cd artifacts\win-x64
.\start-portable.cmd
```

#### Step 5 — Open it in your browser

1. Open Edge or Chrome.
2. Go to: [http://127.0.0.1:5180](http://127.0.0.1:5180)
3. First time: follow the setup screens (pick your vault folder, make an admin password if asked).

You should see folders on the left, notes in the middle, and the editor on the right.

**To stop the server later:** close the window that `start-portable.cmd` opened, or press Ctrl+C in that window.

---

## Developer / day-to-day run (from source)

If you’re changing the code on this machine:

```powershell
cd jotdex\src\Web
npm install
npm run build
cd ..\Server
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --no-launch-profile
```

Then open [http://127.0.0.1:5180](http://127.0.0.1:5180).

In Development it may start with a sample vault. Use **Settings** to point at `C:\JotdexVault` (or your real folder), then **Rescan**.

> Tip: if you saved **LAN** in Settings, the server listens on all network interfaces. Prefer `dotnet run --no-launch-profile` so those settings apply.

---

## First things to try in the app

1. **Settings** (top right) → set **Vault** to your notes folder → save.
2. Click **New folder** (left column) to make `Personal` or `Work` at the top level.  
   - Select **Notes** (top of the tree) first if you want a **root** folder.  
   - Select a folder, then **Move**, leave the box blank → moves that folder to the root.
3. Click **New note**, type something, wait for the green **saved** chip.
4. Press **Ctrl+K** and search.
5. Open a note → **Share HTML** to download a file you can email (no app name on the page).
6. **Settings → Start with Windows** so Jotdex comes back after a reboot (or install the Windows Service — see [SETUP.md](SETUP.md)).
7. **Settings → Logs → View recent log** if something goes wrong (files also live under app data `\logs\`).

### Folders

- **▸ / ▾** next to a folder name collapses or expands it.
- **Rename** / **Move** / **Trash** apply to the folder you currently have selected.

### Notes

- Click the title at the top of the editor (or **Rename**) to change the note’s name and file.
- **Move** puts the note in a different folder (blank = vault root).

---

## Use from another computer

### On your home or office network (LAN)

1. On the Jotdex PC: **Settings → Network** → choose **LAN (all interfaces)** → **Save network** (Windows may ask for Admin to open the firewall for HTTP/HTTPS) → **Restart server**. If you cancel UAC, LAN still saves — open the ports yourself, or use **Open firewall ports** in Settings.
2. On the other device’s browser, open:  
   `http://THE-SERVER-PC-IP:5180`  
   Example: `http://192.168.1.50:5180`  
   (Find the IP in Windows: Settings → Network, or `ipconfig`.)

### Away from home (VPN or Cloudflare Tunnel)

Do **not** port-forward `5180` to the public internet.

**VPN** — join the same network as the Jotdex PC (WireGuard, Tailscale, a work VPN, etc.), then use the LAN URL above.

**Cloudflare Tunnel** (`cloudflared`) — an outbound tunnel from this PC, so you do not open inbound ports. Point the tunnel at Jotdex on loopback and keep the app bound to this PC only:

1. Leave **Settings → Network** on **this PC only** (`127.0.0.1`) unless you also want LAN.
2. Turn on a **password** (and **TOTP** if you want a second factor). A tunnel makes the app reachable from anywhere, so lock it.
3. Install [cloudflared](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/) and create a tunnel whose origin is `http://127.0.0.1:5180`.
4. Open your tunnel hostname in a browser (phone, Mac, another network).

Cloudflare’s setup guide: [Connect apps with Cloudflare Tunnels](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/). Optional: put [Cloudflare Access](https://developers.cloudflare.com/cloudflare-one/policies/access/) in front for an extra login before Jotdex’s own.

---

## Where your stuff lives

| Thing | Typical location |
|---|---|
| Your notes (vault) | e.g. `C:\JotdexVault` |
| App settings, search index, history | `%LOCALAPPDATA%\Jotdex` or `data\` next to the portable exe |
| Mirror / cloud copy (optional) | **Settings → Backup → Vault mirror** destination, and/or **Cloud backups** (API Move Kits) |

Moving to a new PC: **Settings → Move to another PC → Create move kit (ZIP)**, copy the ZIP, unzip on the new machine, run `Restore-Jotdex.ps1`. Details: [docs/backup.md](docs/backup.md).

---

## OneNote → Jotdex

If your notes came from OneNote:

1. Export with [alxnbl/onenote-md-exporter](https://github.com/alxnbl/onenote-md-exporter).
2. Run the migration script (it does **not** change your export; it builds a new vault):

```powershell
.\tools\MigrateExport\Migrate-OneNoteMdExporter.ps1 `
  -SourceRoot "PATH\TO\Exports\md" `
  -Destination "C:\JotdexVault"
```

Details: [docs/import-format/onenote-md-exporter.md](docs/import-format/onenote-md-exporter.md).

---

## More help

| Doc | What’s in it |
|---|---|
| [SETUP.md](SETUP.md) | Fuller install / portable / service notes |
| [docs/upgrading.md](docs/upgrading.md) | Check for updates + Update-Jotdex.ps1 |
| [docs/backup.md](docs/backup.md) | Move kit + backup ZIP |
| [docs/cloud-backup.md](docs/cloud-backup.md) | Multi-provider cloud backup (API) |
| [docs/portability.md](docs/portability.md) | Moving vaults safely |
| [docs/vault-format.md](docs/vault-format.md) | On-disk Markdown dialect (callouts, tasks, Source-only) |
| [docs/vault-mirror.md](docs/vault-mirror.md) | Vault mirror (filesystem copy) |
| [docs/changelog.md](docs/changelog.md) | Why recent fixes were made |
| [AGENTS.md](AGENTS.md) | For contributors / AI agents |

---

## License / notices

Jotdex is licensed under the [MIT License](LICENSE). Copyright (c) 2026 Joshua Cline.

Third-party packages used by Jotdex are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Your vault Markdown and attachments remain your content — the license covers the Jotdex application, not your notes.
