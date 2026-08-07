# ADR 0004: Markdown-plus-assets

## Status

Accepted

## Context

Pure Markdown cannot faithfully reproduce arbitrary webpage layouts. Making HTML the primary format hurts portability and clean editing.

## Decision

- Markdown is the canonical note.
- Complex paste/clip may add a sanitized HTML snapshot under `NoteName.assets/` (e.g. `clipped-page.html`).
- Note embeds the snapshot as expandable section or attachment.
- Without Jotdex, the HTML file opens in any browser.
- No base64 images in Markdown; normal image files only.

## Consequences

Paste modes include preserve-page. Sanitizer/CSP required before rendering HTML sidecars.
