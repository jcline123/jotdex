# Packaging and Windows Service

## Portable ZIP (self-contained)

From the repo root (needs .NET 10 SDK; builds SPA if `wwwroot` is missing):

```powershell
.\scripts\publish-win-x64.ps1
```

Output: `artifacts\win-x64\`

| File | Purpose |
|---|---|
| `Jotdex.Server.exe` | Single-file self-contained host |
| `start-portable.cmd` | Starts on this PC (Production; `.\data` app data) |
| `install-service.ps1` / `uninstall-service.ps1` | Windows Service helpers |
| `appsettings.example.json` | Sample config |
| `README-PORTABLE.txt` | Quick start |

Zip that folder for distribution. Target PCs do **not** need .NET or Node.

### Run portable

```powershell
cd artifacts\win-x64
.\start-portable.cmd
```

Open http://127.0.0.1:5180 → first-run wizard (vault, admin, bind/port).

App data: `artifacts\win-x64\data\` (`config\`, `auth\`, `indexes\`, `history\`, `trash\`).

### Network

- Default: loopback `127.0.0.1` (also configurable in Settings → Network → restart).
- LAN: Settings → Network → LAN, Save (UAC may add firewall rules), Restart. Or `--urls http://0.0.0.0:5180`. Manual: `Ensure-JotdexFirewall.ps1` as Administrator.
- HTTPS: set **HTTPS certificate (PFX path)** in Settings → Network (optional password, or env `JOTDEX_HTTPS_PFX_PASSWORD`). Restart required. When a valid PFX is configured and `ASPNETCORE_URLS` is unset, Kestrel listens with HTTPS on the configured bind/port.
- Prefer a VPN or a [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) (`cloudflared` → `http://127.0.0.1:5180`) for remote access; do not port-forward to the public Internet.

## Windows Service

Elevated PowerShell from the publish folder (or pass `-ExePath`):

```powershell
.\install-service.ps1
# ...
.\uninstall-service.ps1
```

Default service name: `Jotdex`, URL `http://127.0.0.1:5180`.

Vault stays on local disk outside the install folder. Upgrades: stop service, replace exe files, start service — leave vault and `data\` intact.

## Upgrade / backup notes

1. Stop Jotdex (or the service).
2. Prefer **Settings → Backup → Create move kit** (full kit) or **Create backup ZIP** (data only) — see [backup.md](../docs/backup.md).
3. Or manually back up the **vault** folder and optionally `data\auth`, `data\history`, `data\config`.
4. Replace application binaries only (for upgrades).
5. Start → health check → Rescan if needed.
