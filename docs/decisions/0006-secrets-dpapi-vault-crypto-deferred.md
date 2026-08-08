# ADR 0006: Secrets via DPAPI; vault encryption deferred

## Status

Accepted

## Context

Jotdex needs to store SMTP passwords, Telegram bot tokens, and TOTP secrets without putting them in plain JSON. Move kits must transfer those secrets to a new PC. Optional vault encryption was considered.

## Decision

1. **At rest on the install PC:** secret values are wrapped with Windows **DPAPI CurrentUser** (`ProtectedData`) in `data/secrets/`. Non-secret channel settings stay in normal JSON under `data/config/`.
2. **Move kit / backup:** unwrap secrets into `appdata/secrets-portable.json` (plaintext for private transfer). On restore / first start, re-wrap with DPAPI for the new Windows user and delete the portable file. Treat the ZIP as secret.
3. **Vault encryption:** deferred. Per-note decrypt-on-open would still leave plaintext in FTS (`search.db`), history, and exports unless those are redesigned. Prefer OS **BitLocker** for stolen-disk protection while keeping Markdown files as the product.

## Consequences

- Works seamlessly for portable app, Startup shortcut, and `dotnet run` as the same Windows user.
- Windows Service as Local System cannot read CurrentUser blobs; run the service as the interactive user or document later LocalMachine support.
- Move-kit and Restore scripts must know about `secrets-portable.json`.
- In-app vault encryption remains a future ADR if product direction changes.
