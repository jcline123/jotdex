# ADR 0003: Note history and autosave

## Status

Accepted

## Context

Users need recovery from accidental edits and uninterrupted typing without explicit Save for every change.

## Decision

- Autosave after 800–1200 ms idle; status Saved only after atomic server write.
- History snapshots in AppData `history\{noteId}\`, outside the vault.
- Snapshot before content-changing saves; dedupe by content hash.
- Retention: last 50 snapshots per note or 30 days, whichever is smaller.
- Restore takes a pre-restore snapshot so restore itself is undoable.
- V1 history stores Markdown body only (not full `.assets` trees).

## Consequences

Vault moves do not include history unless explicitly backed up. Documented in `docs/portability.md`.
