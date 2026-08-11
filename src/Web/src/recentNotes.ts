const STORAGE_KEY = 'jotdex.recentNoteIds'
export const MAX_RECENT_NOTES = 24

export function loadRecentNoteIds(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return []
    return parsed.filter((x): x is string => typeof x === 'string').slice(0, MAX_RECENT_NOTES)
  } catch {
    return []
  }
}

export function cacheRecentNoteIds(ids: string[]): string[] {
  const next = ids.filter((x) => typeof x === 'string' && x.trim() !== '').slice(0, MAX_RECENT_NOTES)
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
  } catch {
    /* ignore */
  }
  return next
}

export function rememberViewedNote(id: string): string[] {
  const trimmed = id.trim()
  if (!trimmed) return loadRecentNoteIds()
  return cacheRecentNoteIds([trimmed, ...loadRecentNoteIds().filter((x) => x !== trimmed)])
}
