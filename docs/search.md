# Search notes

Search indexes **titles, body text, headings, tags, folder paths, code blocks, and attachment filenames**.

## Behavior

- **Default (smart/hybrid):** prefix FTS (`aqua` → aquarium) plus substring fallbacks so short/partial tokens work (`ip` → IPsec).
- **Multi-word:** all tokens must match (AND) across title or body (or other indexed fields).
- **Literal / trigram:** quote a phrase (`"0x80070005"`) or `?literal=true` for exact substring when trigram is available (needs ≥3 chars for trigram; shorter uses LIKE).
- **Filters:** `folder:`, `tag:`, `title:`, `in:code` / `type:code`, `has:attachment`, `modified:30d`
- Index is disposable under `{dataRoot}/indexes/search.db` — rebuild via Rescan / `/api/admin/reindex`

## OCR (future)

Screenshot OCR is **out of scope for V1**. Images are indexed by filename and containing note; optional OCR can be added later without changing vault files.
