# Cloud backup personal-account matrices

Live runs against Joshua’s personal Dropbox / Google / OneDrive accounts.

**Status:** pending live run (Joshua). Adapter unit tests with mocked HTTP cover CB-23 / CB-33 / CB-43; these matrices are CB-24 / CB-34 / CB-44.

## Dropbox (CB-24)

| Step | Result | Notes |
|---|---|---|
| Connect PKCE (App Folder) | pending | |
| Upload small Move Kit + content_hash verify | pending | |
| Upload >8 MiB via session | pending | |
| Quota + retention prune | pending | |
| Revoke / reconnect | pending | |

## Google Drive (CB-34)

| Step | Result | Notes |
|---|---|---|
| Desktop OAuth + `drive.file` | pending | Requires `JOTDEX_CLOUD_GOOGLE_CLIENT_ID` |
| Resumable upload + MD5 | pending | |
| Folder id reuse after reconnect | pending | |
| Quota + retention | pending | |

## OneDrive (CB-44)

| Step | Result | Notes |
|---|---|---|
| Personal account + App Folder | pending | May need preview-permission consent |
| Upload session + size/hash | pending | |
| Approot recreate after delete | pending | |
| 401 → ReconnectRequired | pending | |

## Restore (CB-61)

| Step | Result | Notes |
|---|---|---|
| Download encrypted `.jotdexkit` from provider | pending | Prefer Move Kit over vault ZIP |
| `Decrypt-JotdexKit.ps1` / `--decrypt-kit` (JDXK2) | pending | Unlock password required |
| `Restore-Jotdex.ps1` onto new PC local disk | pending | Live vault must not be in iCloud |
| Reconnect each cloud provider after restore | pending | OAuth secrets are machine-bound (`data/secrets/cloud-backup.json` not in kit) |
| Optional: open vault-only ZIP without Jotdex | pending | Only if `IncludePlainVaultZip` was enabled |
