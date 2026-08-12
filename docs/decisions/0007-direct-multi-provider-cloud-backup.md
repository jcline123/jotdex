# ADR 0007: Direct multi-provider cloud backup

## Status

Accepted

## Context

Jotdex already has a read-only vault mirror to a filesystem destination (often iCloud/OneDrive sync folders). That is not proof a file reached cloud object storage, and it does not produce a point-in-time full recovery kit. Users want scheduled encrypted Move Kits uploaded through provider APIs (personal OneDrive, Google Drive, Dropbox), independent health per provider, and an optional application-independent readable copy of Markdown notes.

## Decision

1. **Cloud backup is independent from vault mirroring.** Separate settings (`data/config/cloud-backup.json`), state, hosted service, APIs, and UI. Mirror behavior is unchanged.
2. **Live vault stays on local disk.** Cloud backup never writes the live vault into a sync folder and never becomes bidirectional sync.
3. **Each run is a backup generation.** Always create one encrypted Move Kit (`.jotdexkit`, streaming **JDXK2**, with **JDXK1** still decryptable). Optionally create one unencrypted **vault-only** ZIP when `IncludePlainVaultZip` is enabled (default off).
4. **Readable vault ZIP is opt-in and strictly vault-only.** It must never contain app data, auth, history, portable secrets, cloud credentials, or binaries. The encrypted Move Kit remains mandatory and primary; the ZIP is an emergency fallback, not a replacement. Jotdex permits this because Markdown files are the product and must remain independently recoverable.
5. **One shared staged vault snapshot** feeds both artifacts; identical bytes upload to every enabled provider.
6. **OAuth credentials** live in a separate DPAPI store (`data/secrets/cloud-backup.json`) that is **not** included in Move Kits or `ISecretStore.ExportPortable()`.
7. **Remote verification is required** before success; retention prunes only **complete** generations after a newer complete generation is verified.
8. **Provider failures are independent.** One provider failing must not block others.
9. **OneDrive App Folder** (`Files.ReadWrite.AppFolder`) is preferred; broader scopes require an explicit product decision and ADR update.
10. **Direct iCloud/CloudKit** is out of scope (filesystem mirror remains the iCloud path).
11. **Restore** continues via existing Move Kit / Restore-Jotdex tooling; cloud providers must be reconnected after restore on a new PC.

## Consequences

- New data folders: `config/cloud-backup.json`, `state/cloud-backup/`, `secrets/cloud-backup.json`, `exports/cloud-backup-staging/`.
- Setup/move-kit/restore docs and scripts must know which of these are portable vs install-bound.
- Enabling readable ZIP approximately doubles cloud storage per generation.
- Home page shows provider health; Move Kit failure is critical; vault-ZIP-only failure is a partial warning.
- **OAuth clients** are supplied at runtime via env (`JOTDEX_CLOUD_*_CLIENT_ID` / Dropbox app key). Google production clients must be configured with `JOTDEX_CLOUD_GOOGLE_CLIENT_ID` (PKCE desktop flow); see [`docs/cloud-backup.md`](../cloud-backup.md).
- **OneDrive personal accounts:** Azure apps that are not publisher-verified may show Microsoft’s “unverified” / preview-permission consent screens. Users must approve those prompts (or the app must complete verification) before App Folder backup works. Broader Graph scopes remain out of scope unless this ADR is updated.
- Live personal-account matrices (CB-24/34/44) stay manual; results live in [`docs/cloud-backup-matrices.md`](../cloud-backup-matrices.md).
