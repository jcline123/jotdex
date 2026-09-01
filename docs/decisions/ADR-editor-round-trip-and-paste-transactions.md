# ADR 0008: Editor round-trip codec and transactional paste

## Status

Accepted

## Context

Visual editing converted to Markdown through `tiptap-markdown` on every transaction. Block images did not close their Markdown block, so `![x](url)### Heading` survived save and reopened as literal hashes. Attachment metadata triggered `setContent()`. Async image paste inserted at the current caret after await. Autosave could persist intermediate documents.

## Decision

- Jotdex owns `EditorMarkdownCodec` (parse/serialize/validate) even while `tiptap-markdown` remains the lower-level converter.
- Block images use a Jotdex serializer that calls the Markdown block-close mechanism after the image.
- Canonical image `src` stays vault-relative; `/api/attachments/{id}` is display-only via `AttachmentResolver`.
- Paste/upload uses `PasteSessionManager` with non-persistable placeholders resolved by upload id.
- `EditorRevisionCoordinator` separates dirty vs validated Markdown. `SaveCoordinator` / revision checks prevent an older PUT from marking a newer edit Saved.
- Exact save equivalence (LF + ignore `modified:`) is shared TypeScript/C# test vectors. Cosmetic blank-line collapsing is not used for dirty/conflict.
- Official `@tiptap/markdown` is **not** switched in this release (separate parity spike).

## Consequences

Opening a note without editing must not rewrite the file. Failed validation must not PUT. Existing fused notes are not auto-repaired.
