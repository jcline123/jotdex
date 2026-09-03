import { useCallback, useEffect, useRef, useState } from 'react'
import {
  formatDueLabel,
  mergeRailTodos,
  newTodoId,
  normalizeTodoPriority,
  parseTodosMarkdown,
  serializeTodosMarkdown,
  sortTodos,
  type TodoItem,
  type TodoPriority,
} from './todosMarkdown'
import { promptTodoNotifications, startTodoReminderLoop } from './todoReminders'
import {
  AUTH_REQUIRED_EVENT,
  isSessionGone,
  throwIfUnauthorized,
} from './IdleLockGate'

const UNDO_MS = 30_000

type NoteDetail = {
  id: string
  title: string
  relativePath: string
  markdown: string
  etag: string
}

type Props = {
  /** When true, fill the pane (mobile tab). When false, desktop rail. */
  fill?: boolean
  collapsed?: boolean
  onToggleCollapsed?: () => void
  onOpenNote?: (noteId: string) => void
  /** Bump when notes are trashed/restored/saved so note-backed tasks stay in sync. */
  refreshKey?: number
  /** After a note-backed task is rewritten on disk, reload that note if open. */
  onNoteTasksChanged?: (noteId: string) => void
}

type VaultTask = {
  id: string
  noteId: string
  noteTitle: string
  noteRelativePath: string
  text: string
  due?: string | null
  priority?: string
  remind?: string | null
  added?: string | null
  standaloneTodosMd?: boolean
}

type PendingUndo = {
  item: TodoItem
  expiresAt: number
}

type Selection =
  | { kind: 'local'; id: string }
  | { kind: 'vault'; id: string }

async function findOrCreateTodosNote(): Promise<NoteDetail> {
  const byPath = await fetch('/api/notes/by-path?path=Todos.md', { credentials: 'same-origin' })
  throwIfUnauthorized(byPath)
  if (byPath.ok) {
    return (await byPath.json()) as NoteDetail
  }
  const created = await fetch('/api/notes', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      title: 'Todos',
      folder: '',
      markdown: '',
    }),
  })
  throwIfUnauthorized(created)
  const data = (await created.json()) as NoteDetail & { error?: string }
  if (!created.ok) throw new Error(data.error ?? 'Could not create Todos.md')
  if (data.id && typeof data.markdown === 'string' && data.etag) return data
  const note = (await fetch(`/api/notes/${data.id}`, { credentials: 'same-origin' }).then((r) =>
    r.json(),
  )) as NoteDetail
  return note
}

export function TodosRail({
  fill,
  collapsed,
  onToggleCollapsed,
  onOpenNote,
  refreshKey = 0,
  onNoteTasksChanged,
}: Props) {
  const [items, setItems] = useState<TodoItem[]>([])
  const [vaultTasks, setVaultTasks] = useState<VaultTask[]>([])
  const [noteId, setNoteId] = useState<string | null>(null)
  const [etag, setEtag] = useState('')
  const [markdownBase, setMarkdownBase] = useState('')
  const etagRef = useRef('')
  const markdownRef = useRef('')
  etagRef.current = etag
  markdownRef.current = markdownBase
  const [error, setError] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const draftInputRef = useRef<HTMLInputElement>(null)
  const [selection, setSelection] = useState<Selection | null>(null)
  const [busy, setBusy] = useState(false)
  const [pendingUndo, setPendingUndo] = useState<PendingUndo | null>(null)
  const [undoLeftSec, setUndoLeftSec] = useState(0)
  const itemsRef = useRef(items)
  itemsRef.current = items
  const saveTimer = useRef<number | null>(null)
  const undoTimer = useRef<number | null>(null)
  const vaultSaveTimer = useRef<number | null>(null)

  const persist = useCallback(
    async (next: TodoItem[], baseMarkdown: string, id: string, expectedEtag: string) => {
      const markdown = serializeTodosMarkdown(baseMarkdown, next)
      const res = await fetch(`/api/notes/${id}`, {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ markdown, etag: expectedEtag }),
      })
      throwIfUnauthorized(res)
      const data = await res.json()
      if (!res.ok) throw new Error(data.error ?? 'Could not save todos')
      if (data.etag) setEtag(data.etag)
      if (data.markdown) setMarkdownBase(data.markdown)
      else setMarkdownBase(markdown)
      return data as { etag?: string; markdown?: string }
    },
    [],
  )

  const queueSave = useCallback(
    (next: TodoItem[]) => {
      setItems(next)
      if (!noteId) return
      if (saveTimer.current) window.clearTimeout(saveTimer.current)
      saveTimer.current = window.setTimeout(() => {
        void persist(next, markdownRef.current, noteId, etagRef.current).catch((e) => {
          if (isSessionGone(e)) return
          setError(e instanceof Error ? e.message : 'Save failed')
        })
      }, 280)
    },
    [noteId, persist],
  )

  const reload = useCallback(async () => {
    setError(null)
    setBusy(true)
    try {
      const note = await findOrCreateTodosNote()
      setNoteId(note.id)
      setEtag(note.etag)
      setMarkdownBase(note.markdown)
      setItems(sortTodos(parseTodosMarkdown(note.markdown)))
      try {
        const taskRes = await fetch('/api/tasks', { credentials: 'same-origin' })
        throwIfUnauthorized(taskRes)
        const taskData = await taskRes.json()
        const raw = Array.isArray(taskData.items) ? (taskData.items as VaultTask[]) : []
        setVaultTasks(raw.filter((t) => !t.standaloneTodosMd))
      } catch (e) {
        if (isSessionGone(e)) throw e
        setVaultTasks([])
      }
    } catch (e) {
      if (isSessionGone(e)) return
      setError(e instanceof Error ? e.message : 'Could not load todos')
    } finally {
      setBusy(false)
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  useEffect(() => {
    if (refreshKey === 0) return
    const t = window.setTimeout(() => void reload(), 250)
    return () => window.clearTimeout(t)
  }, [refreshKey, reload])

  useEffect(() => {
    const onAuthRequired = () => {
      setError(null)
      if (saveTimer.current) {
        window.clearTimeout(saveTimer.current)
        saveTimer.current = null
      }
      if (vaultSaveTimer.current) {
        window.clearTimeout(vaultSaveTimer.current)
        vaultSaveTimer.current = null
      }
    }
    window.addEventListener(AUTH_REQUIRED_EVENT, onAuthRequired)
    return () => window.removeEventListener(AUTH_REQUIRED_EVENT, onAuthRequired)
  }, [])

  useEffect(() => {
    return startTodoReminderLoop(() => itemsRef.current)
  }, [])

  useEffect(() => {
    if (!pendingUndo) {
      setUndoLeftSec(0)
      return
    }
    const tick = () => {
      const left = Math.max(0, Math.ceil((pendingUndo.expiresAt - Date.now()) / 1000))
      setUndoLeftSec(left)
      if (left <= 0) setPendingUndo(null)
    }
    tick()
    const id = window.setInterval(tick, 250)
    return () => window.clearInterval(id)
  }, [pendingUndo])

  useEffect(() => {
    return () => {
      if (undoTimer.current) window.clearTimeout(undoTimer.current)
      if (saveTimer.current) window.clearTimeout(saveTimer.current)
      if (vaultSaveTimer.current) window.clearTimeout(vaultSaveTimer.current)
    }
  }, [])

  // Drop selection if the task vanished (e.g. note deleted).
  useEffect(() => {
    if (!selection) return
    if (selection.kind === 'local' && !items.some((t) => t.id === selection.id)) setSelection(null)
    if (selection.kind === 'vault' && !vaultTasks.some((t) => t.id === selection.id)) setSelection(null)
  }, [items, vaultTasks, selection])

  const selectedLocal = selection?.kind === 'local' ? (items.find((t) => t.id === selection.id) ?? null) : null
  const selectedVault =
    selection?.kind === 'vault' ? (vaultTasks.find((t) => t.id === selection.id) ?? null) : null

  function maybeAskNotifications(nextRemind: string | undefined) {
    if (!nextRemind || nextRemind === 'off') return
    void promptTodoNotifications({ force: true })
  }

  function addTodo() {
    const title = draft.trim()
    if (!title) return
    const isFirst = items.length === 0
    const item: TodoItem = {
      id: newTodoId(),
      title,
      priority: 'normal',
      due: null,
      remind: 'off',
      added: new Date().toISOString(),
    }
    setDraft('')
    setSelection(null)
    queueSave(sortTodos([item, ...items]))
    window.setTimeout(() => draftInputRef.current?.focus(), 0)
    if (isFirst) void promptTodoNotifications({ force: true })
  }

  function completeTodo(id: string) {
    const item = items.find((t) => t.id === id)
    if (!item) return
    if (selection?.kind === 'local' && selection.id === id) setSelection(null)
    if (undoTimer.current) window.clearTimeout(undoTimer.current)
    setPendingUndo({ item, expiresAt: Date.now() + UNDO_MS })
    undoTimer.current = window.setTimeout(() => setPendingUndo(null), UNDO_MS)
    queueSave(items.filter((t) => t.id !== id))
  }

  async function completeVaultTask(id: string) {
    try {
      const res = await fetch('/api/tasks/complete', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id }),
      })
      throwIfUnauthorized(res)
      const data = await res.json()
      if (!res.ok || !data.success) throw new Error(data.error ?? 'Could not complete task')
      if (selection?.kind === 'vault' && selection.id === id) setSelection(null)
      if (typeof data.noteId === 'string' && data.noteId) onNoteTasksChanged?.(data.noteId)
      else if (data.noteId != null) onNoteTasksChanged?.(String(data.noteId))
      await reload()
    } catch (e) {
      if (isSessionGone(e)) return
      setError(e instanceof Error ? e.message : 'Could not complete task')
    }
  }

  function undoComplete() {
    if (!pendingUndo) return
    if (undoTimer.current) window.clearTimeout(undoTimer.current)
    const restored = pendingUndo.item
    setPendingUndo(null)
    queueSave(sortTodos([restored, ...itemsRef.current]))
    setSelection({ kind: 'local', id: restored.id })
  }

  function updateSelectedLocal(patch: Partial<TodoItem>) {
    if (!selectedLocal) return
    if (patch.remind !== undefined) maybeAskNotifications(patch.remind)
    queueSave(sortTodos(items.map((t) => (t.id === selectedLocal.id ? { ...t, ...patch } : t))))
  }

  function queueVaultUpdate(id: string, patch: { text?: string; priority?: string; due?: string | null; remind?: string }) {
    setVaultTasks((prev) =>
      prev.map((t) => {
        if (t.id !== id) return t
        return {
          ...t,
          text: patch.text ?? t.text,
          priority: patch.priority ?? t.priority,
          due: patch.due === undefined ? t.due : patch.due,
          remind: patch.remind === undefined ? t.remind : patch.remind,
        }
      }),
    )
    if (vaultSaveTimer.current) window.clearTimeout(vaultSaveTimer.current)
    vaultSaveTimer.current = window.setTimeout(() => {
      void (async () => {
        try {
          const body: Record<string, string> = { id }
          if (patch.text !== undefined) body.text = patch.text
          if (patch.priority !== undefined) body.priority = patch.priority
          if (patch.due !== undefined) body.due = patch.due ?? ''
          if (patch.remind !== undefined) body.remind = patch.remind === 'off' ? 'off' : patch.remind
          const res = await fetch('/api/tasks/update', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
          })
          throwIfUnauthorized(res)
          const data = await res.json()
          if (!res.ok || !data.success) throw new Error(data.error ?? 'Could not update task')
          if (typeof data.noteId === 'string' && data.noteId) onNoteTasksChanged?.(data.noteId)
          else if (data.noteId != null) onNoteTasksChanged?.(String(data.noteId))
          await reload()
        } catch (e) {
          if (isSessionGone(e)) return
          setError(e instanceof Error ? e.message : 'Could not update task')
          await reload()
        }
      })()
    }, 320)
  }

  function updateSelectedVault(patch: {
    text?: string
    priority?: TodoPriority
    due?: string | null
    remind?: string
  }) {
    if (!selectedVault) return
    if (patch.remind !== undefined) maybeAskNotifications(patch.remind)
    queueVaultUpdate(selectedVault.id, {
      text: patch.text,
      priority: patch.priority,
      due: patch.due === undefined ? undefined : patch.due,
      remind: patch.remind,
    })
  }

  const railRows = mergeRailTodos(items, vaultTasks)

  if (collapsed && !fill) {
    const titles = railRows
      .map((row) => (row.kind === 'local' ? row.item.title : row.task.text).trim())
      .filter(Boolean)
    const durationSec = Math.max(14, titles.length * 5)
    return (
      <aside
        className="pane todos pane-rail-collapsed todos-ticker-rail"
        role="button"
        tabIndex={0}
        title="Show todos"
        onClick={onToggleCollapsed}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault()
            onToggleCollapsed?.()
          }
        }}
      >
        {titles.length > 0 && (
          <div className="todos-ticker" aria-hidden>
            <div className="todos-ticker-track" style={{ animationDuration: `${durationSec}s` }}>
              {[0, 1].map((copy) => (
                <div key={copy} className="todos-ticker-group">
                  {titles.map((title, i) => (
                    <span key={`${copy}-${i}`} className="todos-ticker-item">
                      {title}
                    </span>
                  ))}
                </div>
              ))}
            </div>
          </div>
        )}
        <div className="todos-ticker-label">
          Todos
          {titles.length > 0 ? ` · ${titles.length}` : ''}
        </div>
      </aside>
    )
  }

  const openCount = items.length + vaultTasks.length

  return (
    <aside className={`pane todos${fill ? ' todos-fill' : ''}`}>
      <div className="todos-head">
        <div className="todos-head-main">
          <h2>Todos{openCount ? ` · ${openCount}` : ''}</h2>
          {!fill && onToggleCollapsed && (
            <button type="button" className="ghost pane-collapse-btn" onClick={onToggleCollapsed} title="Collapse todos">
              ⟩
            </button>
          )}
        </div>
        <form
          className="todos-add"
          onSubmit={(e) => {
            e.preventDefault()
            addTodo()
          }}
        >
          <input
            ref={draftInputRef}
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            placeholder="Add a to-do…"
            aria-label="New to-do"
          />
          <button type="submit" className="ghost" disabled={!draft.trim()}>
            Add
          </button>
        </form>
      </div>

      {error && <p className="todos-error">{error}</p>}
      {busy && items.length === 0 && vaultTasks.length === 0 && <p className="muted todos-empty">Loading…</p>}

      <div className="todos-scroll">
        {!busy && items.length === 0 && vaultTasks.length === 0 && !pendingUndo && (
          <p className="muted todos-empty">Nothing open — add something above, or check a box in a note.</p>
        )}

        {railRows.length > 0 && (
          <ul className="todos-list">
            {railRows.map((row) => {
              if (row.kind === 'local') {
                const t = row.item
                return (
                  <li key={`local:${t.id}`}>
                    <div
                      className={`todos-row priority-${t.priority}${
                        selection?.kind === 'local' && selection.id === t.id ? ' on' : ''
                      }`}
                    >
                      <input
                        type="checkbox"
                        checked={false}
                        aria-label={`Complete ${t.title}`}
                        onChange={() => completeTodo(t.id)}
                      />
                      <button
                        type="button"
                        className="todos-row-main"
                        onClick={() => setSelection({ kind: 'local', id: t.id })}
                      >
                        <span className="todos-title">{t.title}</span>
                        <span className="todos-meta">
                          <span className={`todos-pri pri-${t.priority}`}>{t.priority}</span>
                          {formatDueLabel(t.due) && <span className="todos-due">{formatDueLabel(t.due)}</span>}
                          {t.remind !== 'off' && <span className="todos-remind">remind</span>}
                        </span>
                      </button>
                    </div>
                  </li>
                )
              }

              const t = row.task
              const pri = normalizeTodoPriority(t.priority)
              return (
                <li key={`vault:${t.id}`}>
                  <div
                    className={`todos-row priority-${pri}${
                      selection?.kind === 'vault' && selection.id === t.id ? ' on' : ''
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={false}
                      aria-label={`Complete ${t.text}`}
                      onChange={() => void completeVaultTask(t.id)}
                    />
                    <div className="todos-row-body">
                      <button
                        type="button"
                        className="todos-row-main"
                        onClick={() => setSelection({ kind: 'vault', id: t.id })}
                      >
                        <span className="todos-title">{t.text}</span>
                        <span className="todos-meta">
                          <span className={`todos-pri pri-${pri}`}>{pri}</span>
                          {formatDueLabel(t.due ?? null) && (
                            <span className="todos-due">{formatDueLabel(t.due ?? null)}</span>
                          )}
                          {t.remind && t.remind !== 'off' && <span className="todos-remind">remind</span>}
                        </span>
                      </button>
                      {onOpenNote ? (
                        <button
                          type="button"
                          className="todos-note-open"
                          title={`Open note: ${t.noteTitle || 'Untitled'}`}
                          onClick={(e) => {
                            e.preventDefault()
                            e.stopPropagation()
                            onOpenNote(t.noteId)
                          }}
                        >
                          <span className="todos-note-open-mark" aria-hidden>
                            ↗
                          </span>
                          <span className="todos-note-open-title">{t.noteTitle || 'Open note'}</span>
                        </button>
                      ) : (
                        t.noteTitle && <span className="todos-note-link muted">{t.noteTitle}</span>
                      )}
                    </div>
                  </div>
                </li>
              )
            })}
          </ul>
        )}
      </div>

      {pendingUndo && (
        <div className="todos-undo" role="status">
          <span className="todos-undo-text">
            Done{pendingUndo.item.title ? `: ${pendingUndo.item.title}` : ''}
            {undoLeftSec > 0 ? ` · ${undoLeftSec}s` : ''}
          </span>
          <button type="button" className="ghost" onClick={undoComplete}>
            Undo
          </button>
        </div>
      )}

      {selectedLocal && (
        <div className="todos-detail">
          <div className="todos-detail-head">
            <strong>Edit</strong>
            <button type="button" className="ghost" onClick={() => setSelection(null)}>
              Close
            </button>
          </div>
          <label className="field">
            Title
            <input value={selectedLocal.title} onChange={(e) => updateSelectedLocal({ title: e.target.value })} />
          </label>
          <label className="field">
            Priority
            <select
              value={selectedLocal.priority}
              onChange={(e) => {
                const priority = e.target.value as TodoPriority
                if (priority === 'critical') updateSelectedLocal({ priority, remind: 'every:30m' })
                else updateSelectedLocal({ priority })
              }}
            >
              <option value="low">Low</option>
              <option value="normal">Normal</option>
              <option value="high">High</option>
              <option value="critical">Critical (every 30m)</option>
            </select>
          </label>
          <label className="field">
            Due
            <input
              type="datetime-local"
              value={dueToLocalInput(selectedLocal.due)}
              onChange={(e) =>
                updateSelectedLocal({
                  due: e.target.value ? new Date(e.target.value).toISOString() : null,
                })
              }
            />
          </label>
          <label className="field">
            Reminder
            <select
              value={remindSelectValue(selectedLocal)}
              onChange={(e) => {
                const v = e.target.value
                if (v === 'off') updateSelectedLocal({ remind: 'off' })
                else if (v === 'critical') updateSelectedLocal({ priority: 'critical', remind: 'every:30m' })
                else if (v === 'every:30m') updateSelectedLocal({ remind: 'every:30m' })
                else if (v === 'every:60m') updateSelectedLocal({ remind: 'every:60m' })
                else if (v === 'once-due') {
                  if (selectedLocal.due) updateSelectedLocal({ remind: `once:${selectedLocal.due}` })
                  else updateSelectedLocal({ remind: `once:${new Date().toISOString()}` })
                }
              }}
            >
              <option value="off">Off</option>
              <option value="once-due">Once at due time</option>
              <option value="every:30m">Every 30 minutes</option>
              <option value="every:60m">Every 60 minutes</option>
              <option value="critical">Critical preset (30m)</option>
            </select>
          </label>
          <p className="muted todos-hint">
            Reminders use browser notifications while this tab is open. Chrome is asked when you add your first to-do
            (or from Settings). Away catch-up: one alert per to-do.
          </p>
          <button type="button" className="ghost" onClick={() => completeTodo(selectedLocal.id)}>
            Mark done
          </button>
        </div>
      )}

      {selectedVault && (
        <div className="todos-detail">
          <div className="todos-detail-head">
            <strong>Edit · from note</strong>
            <button type="button" className="ghost" onClick={() => setSelection(null)}>
              Close
            </button>
          </div>
          <label className="field">
            Title
            <input
              value={selectedVault.text}
              onChange={(e) => updateSelectedVault({ text: e.target.value })}
            />
          </label>
          <label className="field">
            Priority
            <select
              value={normalizeTodoPriority(selectedVault.priority)}
              onChange={(e) => {
                const priority = e.target.value as TodoPriority
                if (priority === 'critical') updateSelectedVault({ priority, remind: 'every:30m' })
                else updateSelectedVault({ priority })
              }}
            >
              <option value="low">Low</option>
              <option value="normal">Normal</option>
              <option value="high">High</option>
              <option value="critical">Critical (every 30m)</option>
            </select>
          </label>
          <label className="field">
            Due
            <input
              type="datetime-local"
              value={dueToLocalInput(selectedVault.due ?? null)}
              onChange={(e) =>
                updateSelectedVault({
                  due: e.target.value ? new Date(e.target.value).toISOString() : null,
                })
              }
            />
          </label>
          <label className="field">
            Reminder
            <select
              value={vaultRemindSelectValue(selectedVault)}
              onChange={(e) => {
                const v = e.target.value
                if (v === 'off') updateSelectedVault({ remind: 'off' })
                else if (v === 'critical') updateSelectedVault({ priority: 'critical', remind: 'every:30m' })
                else if (v === 'every:30m') updateSelectedVault({ remind: 'every:30m' })
                else if (v === 'every:60m') updateSelectedVault({ remind: 'every:60m' })
                else if (v === 'once-due') {
                  if (selectedVault.due) updateSelectedVault({ remind: `once:${selectedVault.due}` })
                  else updateSelectedVault({ remind: `once:${new Date().toISOString()}` })
                }
              }}
            >
              <option value="off">Off</option>
              <option value="once-due">Once at due time</option>
              <option value="every:30m">Every 30 minutes</option>
              <option value="every:60m">Every 60 minutes</option>
              <option value="critical">Critical preset (30m)</option>
            </select>
          </label>
          <p className="muted todos-hint">
            Saved into the note as a checklist comment. Use the note chip on the item, or Open note, to see it in context.
          </p>
          <div className="todos-detail-actions">
            <button type="button" className="ghost" onClick={() => onOpenNote?.(selectedVault.noteId)}>
              Open note
            </button>
            <button type="button" className="ghost" onClick={() => void completeVaultTask(selectedVault.id)}>
              Mark done
            </button>
          </div>
        </div>
      )}
    </aside>
  )
}

function dueToLocalInput(due: string | null): string {
  if (!due) return ''
  const d = new Date(due)
  if (!Number.isFinite(d.getTime())) return ''
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function remindSelectValue(t: TodoItem): string {
  if (!t.remind || t.remind === 'off') return 'off'
  if (t.remind.startsWith('once:')) return 'once-due'
  if (t.remind === 'every:30m' && t.priority === 'critical') return 'critical'
  if (t.remind === 'every:30m') return 'every:30m'
  if (t.remind === 'every:60m') return 'every:60m'
  return 'off'
}

function vaultRemindSelectValue(t: VaultTask): string {
  const remind = t.remind || 'off'
  if (remind === 'off') return 'off'
  if (remind.startsWith('once:')) return 'once-due'
  if (remind === 'every:30m' && normalizeTodoPriority(t.priority) === 'critical') return 'critical'
  if (remind === 'every:30m') return 'every:30m'
  if (remind === 'every:60m') return 'every:60m'
  return 'off'
}
