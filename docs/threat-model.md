# Threat model (initial)

## Assets

- Note Markdown and attachments in the vault
- Auth credentials / session cookies
- Search index and history (sensitive but rebuildable / secondary)

## Trust boundaries

- Browser ↔ Kestrel API
- API ↔ filesystem vault
- API ↔ AppData
- Optional remote image fetch (SSRF risk)
- Optional LAN exposure

## Key threats and mitigations

| Threat | Mitigation |
|---|---|
| Path traversal / junction escape | Resolve paths; reject escapes; never serve by raw client path |
| XSS via note HTML / paste / sidecars | Allowlist sanitizer; CSP; no script execution from notes |
| HTML attachment executing in app origin | `Content-Disposition` / nosniff; conservative inline allowlist |
| CSRF on writes | Cookie auth + antiforgery (M6) |
| Brute-force login | Rate limit / lockout (M6) |
| SSRF on image download | HTTP(S) only; block private/loopback; size/redirect limits |
| LAN sniffing credentials | Default bind localhost; warn on HTTP+LAN; prefer HTTPS/VPN |
| Accidental content loss | Atomic saves; history; no silent Markdown drop |
| Sync corruption | Live vault not on iCloud |

## Non-goals (V1)

- Multi-tenant isolation
- E2E per-note encryption
- Public Internet hosting automation
