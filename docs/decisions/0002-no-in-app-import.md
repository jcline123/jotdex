# ADR 0002: No in-app OneNote importer

## Status

Accepted

## Context

Joshua already has a Markdown export. Building a full import UI/API is large and not needed for day-one editing.

## Decision

- No in-app OneNote import product feature (no import jobs API/UI in V1).
- Offline one-time migration via `tools/MigrateExport/` / agent OPS checklist when the export path is provided.
- Never mutate the original export; stage → validate → commit to vault.

## Consequences

Faster path to a usable editor. Migration quality depends on OPS tooling when the export arrives.
