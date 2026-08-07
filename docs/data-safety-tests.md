# Data-safety tests (must exist before visual save)

These tests are required before enabling general rich-editor save (M5 gate). List tracked for implementation in Unit/Integration/RoundTrip suites.

## Atomicity and concurrency

- [ ] Atomic save: crash between temp write and replace leaves prior good file
- [x] ETag mismatch returns conflict; no silent overwrite
- [ ] Locked file / transient IO failure surfaces error; buffer retained

## Round-trip / no silent loss

- [x] Open→close without edits does not rewrite file
- [x] YAML front matter + unknown keys preserved
- [x] Fenced code (tabs, backslashes, language) preserved
- [x] GFM tables, task lists, nested lists preserved
- [x] Inline/block raw HTML preserved or forced to source mode
- [x] Relative image links with spaces/encoding preserved
- [x] Unresolved wikilinks preserved
- [x] HTML sidecar references preserved

## Path and assets

- [x] Rename/move rewrites relative asset links; assets folder moves
- [x] Duplicate titles never overwrite
- [x] Path traversal rejected
- [ ] Symlink/junction escape rejected

## History

- [x] Content-changing save creates snapshot
- [ ] Identical hash does not duplicate snapshot
- [ ] Restore writes prior body and snapshots current first
- [ ] Retention prune respects 50 / 30d policy

## Security

- [x] Script in pasted/imported HTML does not execute
- [x] SVG script payload blocked by default
- [ ] Attachment MIME / disposition policy enforced
- [x] Remote image localize blocks loopback/private SSRF targets
