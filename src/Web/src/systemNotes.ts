/** Standalone vault todo list — edited via the Todos rail, not the notes UI. */
export function isStandaloneTodosNote(relativePath: string | null | undefined): boolean {
  const rel = (relativePath ?? '').replace(/\\/g, '/').replace(/^\.\//, '').trim()
  if (!rel) return false
  const lower = rel.toLowerCase()
  if (lower === 'todos.md') return true
  // Orphan created if the rail could not find Todos.md while it was hidden from /api/notes
  if (/^todos \(\d+\)\.md$/i.test(rel)) return true
  return false
}
