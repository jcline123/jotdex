import { loadClipDefaultFolder, saveClipDefaultFolder } from './jotdexBookmarklet'
import { loadIdleLockEnabled, loadIdleLockMinutes, saveIdleLockPrefs } from './IdleLockGate'
import { cacheRecentNoteIds, loadRecentNoteIds, rememberViewedNote } from './recentNotes'

export type UiPrefs = {
  configured?: boolean
  idleLockEnabled: boolean
  idleLockMinutes: number
  clipDefaultFolder: string
  recentNoteIds?: string[]
}

function asUiPrefs(data: Partial<UiPrefs> & { configured?: boolean }): UiPrefs {
  return {
    configured: !!data.configured,
    idleLockEnabled: !!data.idleLockEnabled,
    idleLockMinutes: typeof data.idleLockMinutes === 'number' ? data.idleLockMinutes : 15,
    clipDefaultFolder: data.clipDefaultFolder?.trim() || 'Inbox',
    recentNoteIds: Array.isArray(data.recentNoteIds)
      ? data.recentNoteIds.filter((x): x is string => typeof x === 'string' && x.trim() !== '')
      : [],
  }
}

export function localUiPrefs(): UiPrefs {
  return {
    configured: false,
    idleLockEnabled: loadIdleLockEnabled(),
    idleLockMinutes: loadIdleLockMinutes(),
    clipDefaultFolder: loadClipDefaultFolder(),
    recentNoteIds: loadRecentNoteIds(),
  }
}

export function cacheUiPrefs(prefs: UiPrefs) {
  saveIdleLockPrefs(prefs.idleLockEnabled, prefs.idleLockMinutes)
  saveClipDefaultFolder(prefs.clipDefaultFolder)
  cacheRecentNoteIds(prefs.recentNoteIds ?? [])
}

/** Persist shared prefs on the server (and cache locally). Partial updates merge server-side. */
export async function saveUiPrefs(partial: Partial<UiPrefs>): Promise<UiPrefs | null> {
  const res = await fetch('/api/settings/ui', {
    method: 'PUT',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      idleLockEnabled: partial.idleLockEnabled,
      idleLockMinutes: partial.idleLockMinutes,
      clipDefaultFolder: partial.clipDefaultFolder,
      recentNoteIds: partial.recentNoteIds,
    }),
  })
  const data = (await res.json().catch(() => null)) as (Partial<UiPrefs> & { success?: boolean }) | null
  if (!res.ok || !data) return null
  const next = asUiPrefs({ ...data, configured: true })
  cacheUiPrefs(next)
  return next
}

/**
 * Server file wins when present. If recents (or idle lock) only exist in this browser,
 * upload them once so other devices inherit them.
 */
export async function hydrateUiPrefs(server: Partial<UiPrefs> | null | undefined): Promise<UiPrefs> {
  const local = localUiPrefs()
  if (server?.configured) {
    const fromServer = asUiPrefs({ ...server, configured: true })
    if ((fromServer.recentNoteIds?.length ?? 0) === 0 && (local.recentNoteIds?.length ?? 0) > 0) {
      const saved = await saveUiPrefs({ recentNoteIds: local.recentNoteIds })
      if (saved) return saved
    }
    cacheUiPrefs(fromServer)
    return fromServer
  }

  const shouldMigrate =
    local.idleLockEnabled ||
    (local.clipDefaultFolder && local.clipDefaultFolder !== 'Inbox') ||
    (local.recentNoteIds?.length ?? 0) > 0
  if (shouldMigrate) {
    const saved = await saveUiPrefs(local)
    if (saved) return saved
  }
  return local
}

let recentSyncTimer: number | null = null

/** Record a viewed note locally and debounce a server write so home recents match on every device. */
export function rememberViewedNoteAndSync(id: string): string[] {
  const next = rememberViewedNote(id)
  if (recentSyncTimer != null) window.clearTimeout(recentSyncTimer)
  recentSyncTimer = window.setTimeout(() => {
    recentSyncTimer = null
    void saveUiPrefs({ recentNoteIds: next })
  }, 400)
  return next
}
