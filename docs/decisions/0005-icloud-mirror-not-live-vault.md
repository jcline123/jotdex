# ADR 0005: iCloud is mirror only, not live vault

## Status

Accepted

## Context

Bidirectional sync (iCloud) conflicts with multi-file note + assets writes and can create duplicates/conflicts.

## Decision

- Authoritative vault lives on local disk (e.g. `C:\JotdexVault`).
- Never configure the live vault path inside an iCloud-synced folder.
- Optional scheduled **read-only** copy/mirror of the vault into iCloud for offline browsing.
- Away-from-home: secure remote UI, iCloud mirror browse, or static HTML export.

## Consequences

Source code should live on local disk (e.g. `C:\Users\joshu\dev\jotdex`), not in iCloudDrive — GitHub is the backup. Vault data must never live in iCloud as the live vault. Mirror scripts land in M7.
