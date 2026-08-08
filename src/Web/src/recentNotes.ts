const STORAGE_KEY = 'jotdex.recentNoteIds'
const MAX = 24

export function loadRecentNoteIds(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return []
    return parsed.filter((x): x is string => typeof x === 'string').slice(0, MAX)
  } catch {
    return []
  }
}

export function rememberViewedNote(id: string): string[] {
  const next = [id, ...loadRecentNoteIds().filter((x) => x !== id)].slice(0, MAX)
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
  } catch {
    /* ignore */
  }
  return next
}
