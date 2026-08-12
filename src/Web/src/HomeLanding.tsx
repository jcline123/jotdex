import { useEffect, useState } from 'react'
import { formatDueLabel, parseTodosMarkdown, sortTodos, type TodoItem } from './todosMarkdown'
import { cacheRecentNoteIds, loadRecentNoteIds } from './recentNotes'
import { isStandaloneTodosNote } from './systemNotes'
import { CloudBackupHealthBanner } from './CloudBackupHealthBanner'

export type HomeNote = {
  id: string
  title: string
  relativePath: string
  folderPath: string
  modified?: string
  created?: string
}

type Props = {
  vaultName?: string
  noteCount?: number
  folderCount?: number
  onOpenNote: (id: string) => void
  onNewNote: () => void
  onFocusSearch: () => void
  onOpenTodos: () => void
  onOpenCloudBackupSettings?: () => void
  onRetryCloudBackup?: () => void | Promise<void>
}

function ts(value?: string): number {
  if (!value) return 0
  const n = Date.parse(value)
  return Number.isFinite(n) ? n : 0
}

function formatWhen(value?: string): string {
  if (!value) return ''
  const t = Date.parse(value)
  if (!Number.isFinite(t)) return ''
  const d = new Date(t)
  const now = new Date()
  const sameDay =
    d.getFullYear() === now.getFullYear() &&
    d.getMonth() === now.getMonth() &&
    d.getDate() === now.getDate()
  if (sameDay) return d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
  return d.toLocaleDateString([], { month: 'short', day: 'numeric' })
}

function visibleHomeNotes(all: HomeNote[], hiddenIds: Set<string>): HomeNote[] {
  return all.filter((n) => !isStandaloneTodosNote(n.relativePath) && !hiddenIds.has(n.id))
}

async function fetchAllNotes(): Promise<HomeNote[]> {
  const res = await fetch('/api/notes', { credentials: 'same-origin' })
  if (!res.ok) return []
  return (await res.json()) as HomeNote[]
}

async function fetchTodosNoteId(): Promise<string | null> {
  const byPath = await fetch('/api/notes/by-path?path=Todos.md', { credentials: 'same-origin' })
  if (!byPath.ok) return null
  const note = (await byPath.json()) as { id?: string }
  return typeof note.id === 'string' ? note.id : null
}

async function fetchOpenTodos(): Promise<TodoItem[]> {
  const byPath = await fetch('/api/notes/by-path?path=Todos.md', { credentials: 'same-origin' })
  if (!byPath.ok) return []
  const note = await byPath.json()
  return sortTodos(parseTodosMarkdown(String(note.markdown ?? ''))).slice(0, 8)
}

export function HomeLanding({
  vaultName,
  noteCount,
  folderCount,
  onOpenNote,
  onNewNote,
  onFocusSearch,
  onOpenTodos,
  onOpenCloudBackupSettings,
  onRetryCloudBackup,
}: Props) {
  const [notes, setNotes] = useState<HomeNote[]>([])
  const [todos, setTodos] = useState<TodoItem[]>([])
  const [recentIds, setRecentIds] = useState<string[]>(() => loadRecentNoteIds())
  const [busy, setBusy] = useState(true)

  useEffect(() => {
    let cancelled = false
    setBusy(true)
    void (async () => {
      try {
        const [all, openTodos, todosId, uiRes] = await Promise.all([
          fetchAllNotes(),
          fetchOpenTodos(),
          fetchTodosNoteId(),
          fetch('/api/settings/ui', { credentials: 'same-origin' }),
        ])
        if (cancelled) return
        const hiddenIds = new Set<string>()
        if (todosId) hiddenIds.add(todosId)
        setNotes(visibleHomeNotes(all, hiddenIds))
        setTodos(openTodos)
        if (uiRes.ok) {
          const ui = (await uiRes.json()) as { recentNoteIds?: string[] }
          if (Array.isArray(ui.recentNoteIds) && ui.recentNoteIds.length > 0) {
            const ids = cacheRecentNoteIds(ui.recentNoteIds)
            setRecentIds(ids)
          }
        }
      } catch {
        if (!cancelled) {
          setNotes([])
          setTodos([])
        }
      } finally {
        if (!cancelled) setBusy(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  const byId = new Map(notes.map((n) => [n.id, n]))
  const recentViewed = recentIds
    .map((id) => byId.get(id))
    .filter((n): n is HomeNote => !!n)
    .slice(0, 8)

  const recentCreated = [...notes]
    .sort((a, b) => ts(b.created) - ts(a.created) || ts(b.modified) - ts(a.modified))
    .slice(0, 8)

  const recentUpdated = [...notes]
    .sort((a, b) => ts(b.modified) - ts(a.modified))
    .slice(0, 8)

  return (
    <div className="home-landing">
      <header className="home-hero">
        <p className="home-kicker">Welcome back</p>
        <h1 className="home-brand">Jotdex</h1>
        <p className="home-lede">
          {vaultName ? (
            <>
              <span className="home-vault">{vaultName}</span>
              {(noteCount != null || folderCount != null) && (
                <span className="home-stats">
                  {[noteCount != null ? `${noteCount} notes` : null, folderCount != null ? `${folderCount} folders` : null]
                    .filter(Boolean)
                    .join(' · ')}
                </span>
              )}
            </>
          ) : (
            'Your Markdown notebook'
          )}
        </p>
        <p className="home-tagline">Pick up where you left off, clear a to-do, or start a fresh note.</p>
        <div className="home-actions">
          <button type="button" className="primary" onClick={onNewNote}>
            New note
          </button>
          <button type="button" className="ghost" onClick={onFocusSearch}>
            Search
            <kbd>Ctrl+K</kbd>
          </button>
          <button type="button" className="ghost" onClick={onOpenTodos}>
            Todos{todos.length ? ` · ${todos.length}` : ''}
          </button>
        </div>
      </header>

      <CloudBackupHealthBanner
        onOpenSettings={onOpenCloudBackupSettings}
        onRetry={onRetryCloudBackup}
      />

      {busy ? (
        <p className="muted home-loading">Loading your vault…</p>
      ) : (
        <div className="home-panels">
          <section className="home-panel">
            <h2>Recently viewed</h2>
            {recentViewed.length === 0 ? (
              <p className="muted home-empty">Notes you open will show up here.</p>
            ) : (
              <ul className="home-list">
                {recentViewed.map((n) => (
                  <li key={n.id}>
                    <button type="button" className="home-row" onClick={() => onOpenNote(n.id)}>
                      <span className="home-row-title">{n.title}</span>
                      <span className="home-row-meta">{n.folderPath || '/'}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="home-panel">
            <h2>Recently created</h2>
            {recentCreated.length === 0 ? (
              <p className="muted home-empty">No notes yet — create one to begin.</p>
            ) : (
              <ul className="home-list">
                {recentCreated.map((n) => (
                  <li key={n.id}>
                    <button type="button" className="home-row" onClick={() => onOpenNote(n.id)}>
                      <span className="home-row-title">{n.title}</span>
                      <span className="home-row-meta">
                        {formatWhen(n.created || n.modified)}
                        {n.folderPath ? ` · ${n.folderPath}` : ''}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="home-panel">
            <h2>Recently updated</h2>
            {recentUpdated.length === 0 ? (
              <p className="muted home-empty">Nothing edited yet.</p>
            ) : (
              <ul className="home-list">
                {recentUpdated.map((n) => (
                  <li key={n.id}>
                    <button type="button" className="home-row" onClick={() => onOpenNote(n.id)}>
                      <span className="home-row-title">{n.title}</span>
                      <span className="home-row-meta">{formatWhen(n.modified)}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="home-panel home-panel-todos">
            <div className="home-panel-head">
              <h2>Open to-dos</h2>
              <button type="button" className="ghost home-panel-link" onClick={onOpenTodos}>
                Open list
              </button>
            </div>
            {todos.length === 0 ? (
              <p className="muted home-empty">No open to-dos — add some from the Todos rail.</p>
            ) : (
              <ul className="home-list">
                {todos.map((t) => (
                  <li key={t.id}>
                    <button type="button" className="home-row" onClick={onOpenTodos}>
                      <span className="home-row-title">{t.title}</span>
                      <span className="home-row-meta">
                        <span className={`todos-pri pri-${t.priority}`}>{t.priority}</span>
                        {formatDueLabel(t.due) ? ` · ${formatDueLabel(t.due)}` : ''}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      )}

      <footer className="home-foot">
        <p>
          <kbd>Ctrl+K</kbd> search · <kbd>Ctrl+N</kbd> quick open · folders and notes on the left · Todos on the
          right
        </p>
      </footer>
    </div>
  )
}
