# Jotdex

**Jotdex** is a notes app that runs on **your own Windows computer**.

Your notes are normal **Markdown files** in a folder on your hard drive — not locked inside a company’s cloud. You open them in a web browser (on this PC, or another PC on your home/office network if you turn that on).

Think of it like this:

| Piece | What it is |
|---|---|
| **Vault** | A folder full of your notes (`.md` files + pictures) |
| **Jotdex** | A small program that shows and edits that folder in the browser |
| **Browser** | Where you read and write (Chrome, Edge, etc.) |

You own the files. You can copy the vault, back it up, open notes in other Markdown apps, or move to a new PC.

---

## What can it do?

- Create folders and notes
- Edit with a friendly visual editor (headings, lists, tables, code boxes, images)
- Search across all notes
- Autosave + history (undo a bad edit)
- Attach / drag-drop pictures into a note
- **Share HTML** — download one note as a nice single file to send someone (no Jotdex branding)
- Optional: copy the vault one-way to iCloud/OneDrive as a backup (live vault stays on local disk)
- Optional: allow other computers on your network to open the same notes

---

## Before you start (simple checklist)

1. You have a **Windows 10 or 11** PC.
2. You decide where notes will live, for example:  
   `C:\JotdexVault`  
   (Create an empty folder there in File Explorer if you want a fresh start.)
3. **Important:** put the live notes folder on **local disk** (like `C:\…`).  
   Do **not** put the live vault inside iCloud Drive / OneDrive sync folders.  
   Use Jotdex’s **Cloud backup mirror** later if you want a copy in the cloud.

---

## Easiest way to run it (recommended)

This builds a portable folder you can copy and run.

### Step 1 — Get the code

1. Install [Git for Windows](https://git-scm.com/download/win) if you don’t have it.
2. Open **PowerShell**.
3. Run:

```powershell
cd $HOME\Downloads
git clone https://github.com/jcline123/jotdex.git
cd jotdex
```

### Step 2 — Install build tools (one time)

You need these only on the PC that **builds** Jotdex (not necessarily every PC that runs it later):

1. [.NET 10 SDK](https://dotnet.microsoft.com/download) — install it, then close and reopen PowerShell.
2. [Node.js LTS](https://nodejs.org/) — install it, then close and reopen PowerShell.

Check they worked:

```powershell
dotnet --version
node --version
```

Both should print a version number (not an error).

### Step 3 — Build the portable app

Still in the `jotdex` folder:

```powershell
.\scripts\publish-win-x64.ps1
```

Wait until it finishes. Your ready-to-run app is here:

```text
jotdex\artifacts\win-x64\
```

### Step 4 — Start Jotdex

```powershell
cd artifacts\win-x64
.\start-portable.cmd
```

### Step 5 — Open it in your browser

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

### Folders

- **▸ / ▾** next to a folder name collapses or expands it.
- **Rename** / **Move** / **Trash** apply to the folder you currently have selected.

### Notes

- **Rename** changes the note’s title (and file name).
- **Move** puts the note in a different folder (blank = vault root).

---

## Use from another computer on your network

1. On the Jotdex PC: **Settings → Network** → choose **LAN (all interfaces)** → **Save network** → **Restart server**.
2. Allow Windows Firewall if it asks (private network).
3. On the other PC’s browser, open:  
   `http://THE-SERVER-PC-IP:5180`  
   Example: `http://192.168.1.50:5180`  
   (Find the IP in Windows: Settings → Network, or `ipconfig`.)

Do **not** expose this port to the public internet without extra protection (VPN / reverse proxy / HTTPS).

---

## Where your stuff lives

| Thing | Typical location |
|---|---|
| Your notes (vault) | e.g. `C:\JotdexVault` |
| App settings, search index, history | `%LOCALAPPDATA%\Jotdex` or `data\` next to the portable exe |
| Cloud copy (optional) | Wherever you set under **Settings → Cloud backup mirror** |

Moving to a new PC: copy the **vault** folder, install/run Jotdex, point Settings at that folder, Rescan.

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
| [docs/changelog.md](docs/changelog.md) | Why recent fixes were made |
| [docs/portability.md](docs/portability.md) | Moving vaults safely |
| [docs/vault-mirror.md](docs/vault-mirror.md) | Cloud backup mirror |
| [AGENTS.md](AGENTS.md) | For contributors / AI agents |

---

## License / notices

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party components.
