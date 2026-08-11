# Threat model (initial)

## Assets

- Note Markdown and attachments in the vault
- Auth credentials / session cookies / optional TOTP secrets
- App secrets (SMTP passwords, Telegram bot tokens) in `data/secrets` (DPAPI-wrapped)
- Search index and history (sensitive but rebuildable / secondary)

## Trust boundaries

- Browser ↔ Kestrel API
- API ↔ filesystem vault
- API ↔ AppData
- Optional remote image fetch (SSRF risk)
- Optional LAN exposure
- Optional outbound SMTP / Telegram for ops alerts

## Key threats and mitigations

| Threat | Mitigation |
|---|---|
| Path traversal / junction escape | Resolve paths; reject escapes; never serve by raw client path |
| XSS via note HTML / paste / sidecars | Allowlist sanitizer; CSP; no script execution from notes |
| HTML attachment executing in app origin | `Content-Disposition` / nosniff; conservative inline allowlist |
| CSRF on writes | Cookie auth + antiforgery (M6) |
| Brute-force login | Rate limit / lockout (M6); TOTP optional second factor (M8) |
| SSRF on image download | HTTP(S) only; block private/loopback; size/redirect limits |
| LAN sniffing credentials | Default bind localhost; warn on HTTP+LAN; prefer HTTPS, VPN, or Cloudflare Tunnel to loopback |
| Accidental content loss | Atomic saves; history; no silent Markdown drop |
| Sync corruption | Live vault not on iCloud |
| Secrets at rest on install PC | Windows DPAPI CurrentUser (M8) |
| Secrets in move-kit ZIP | Portable plaintext unwrap for transfer; treat ZIP as secret; rewrap on restore |
| Disk theft of whole PC | Prefer OS BitLocker (or similar); see non-goals |

## Non-goals (current)

- Multi-tenant isolation
- **In-app vault / per-note encryption** — deferred. Decrypt-on-open alone does not protect search (`search.db`), history, or exports without a much larger redesign. Prefer **BitLocker** (or VeraCrypt) for stolen-disk protection while keeping Markdown files usable.
- Public Internet hosting automation
