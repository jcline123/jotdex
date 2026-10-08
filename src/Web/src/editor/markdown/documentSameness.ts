/** Server-owned FM keys ignored for exact-save equivalence (keep in sync with ProvenanceKeys.ServerOwned). */
const SERVER_OWNED_FM =
  /^(modified|jotdex_created_via|jotdex_created_by|jotdex_updated_via|jotdex_updated_by|jotdex_last_api_update_at|jotdex_last_api_update_by|jotdex_provenance_hash):\s*.*$/gim

/**
 * Exact save equivalence — shared with C# DocumentSameness.
 * Line endings to LF, trim end, ignore YAML `modified:` and provenance keys. Do not collapse interior blanks.
 */
export function exactSaveNormalize(content: string): string {
  let n = content.replace(/\r\n/g, '\n').replace(/\r/g, '\n').trimEnd()
  if (n.startsWith('---')) {
    const end = n.indexOf('\n---', 3)
    if (end > 0) {
      const header = n.slice(3, end).replace(SERVER_OWNED_FM, (line) => {
        const key = line.split(':')[0] ?? 'modified'
        return `${key}:`
      })
      n = '---' + header + n.slice(end)
    }
  }
  return n
}

export function exactSaveEqual(a: string, b: string): boolean {
  return exactSaveNormalize(a) === exactSaveNormalize(b)
}
