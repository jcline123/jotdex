import { useCallback, useEffect, useRef, useState } from 'react'
import {
  formatDueLabel,
  newTodoId,
  parseTodosMarkdown,
  serializeTodosMarkdown,
  sortTodos,
  type TodoItem,
  type TodoPriority,
} from './todosMarkdown'
import { promptTodoNotifications, startTodoReminderLoop } from './todoReminders'

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
}

type PendingUndo = {
  item: TodoItem
  expiresAt: number
}

async function findOrCreateTodosNote(): Promise<NoteDetail> {
  const list = (await fetch('/api/notes', { credentials: 'same-origin' }).then((r) => r.json())) as {
    id: string
    title: string
    relativePath: string
  }[]
  const existing = list.find(
    (n) => n.relativePath === 'Todos.md' || n.relativePath.replace(/\\/g, '/') === 'Todos.md',
  )
  if (existing) {
    const note = (await fetch(`/api/notes/${existing.id}`, { credentials: 'same-origin' }).then((r) =>
      r.json(),
    )) as NoteDetail
    return note
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
  const data = (await created.json()) as NoteDetail & { error?: string }
  if (!created.ok) throw new Error(data.error ?? 'Could not create Todos.md')
  if (data.id && typeof data.markdown === 'string' && data.etag) return data
  const note = (await fetch(`/api/notes/${data.id}`, { credentials: 'same-origin' }).then((r) =>
    r.json(),
  )) as NoteDetail
  return note
}

export function TodosRail({ fill, collapsed, onToggleCollapsed }: Props) {
  const [items, setItems] = useState<TodoItem[]>([])
  const [noteId, setNoteId] = useState<string | null>(null)
  const [etag, setEtag] = useState('')
  const [markdownBase, setMarkdownBase] = useState('')
  const etagRef = useRef('')
  const markdownRef = useRef('')
  etagRef.current = etag
  markdownRef.current = markdownBase
  const [error, setError] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [pendingUndo, setPendingUndo] = useState<PendingUndo | null>(null)
  const [undoLeftSec, setUndoLeftSec] = useState(0)
  const itemsRef = useRef(items)
  itemsRef.current = items
  const saveTimer = useRef<number | null>(null)
  const undoTimer = useRef<number | null>(null)

  const persist = useCallback(
    async (next: TodoItem[], baseMarkdown: string, id: string, expectedEtag: string) => {
      const markdown = serializeTodosMarkdown(baseMarkdown, next)
      const res = await fetch(`/api/notes/${id}`, {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ markdown, etag: expectedEtag }),
      })
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
        void persist(next, markdownRef.current, noteId, etagRef.current).catch((e) =>
          setError(e instanceof Error ? e.message : 'Save failed'),
        )
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
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load todos')
    } finally {
      setBusy(false)
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

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
    }
  }, [])

  const selected = items.find((t) => t.id === selectedId) ?? null

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
    }
    setDraft('')
    queueSave(sortTodos([item, ...items]))
    setSelectedId(item.id)
    // First open item: ask the browser now (user gesture) so Chrome shows Allow/Block.
    // Settings still re-prompts if they switch browsers or previously blocked/skipped.
    if (isFirst) void promptTodoNotifications({ force: true })
  }

  function completeTodo(id: string) {
    const item = items.find((t) => t.id === id)
    if (!item) return
    if (selectedId === id) setSelectedId(null)
    if (undoTimer.current) window.clearTimeout(undoTimer.current)
    setPendingUndo({ item, expiresAt: Date.now() + UNDO_MS })
    undoTimer.current = window.setTimeout(() => setPendingUndo(null), UNDO_MS)
    queueSave(items.filter((t) => t.id !== id))
  }

  function undoComplete() {
    if (!pendingUndo) return
    if (undoTimer.current) window.clearTimeout(undoTimer.current)
    const restored = pendingUndo.item
    setPendingUndo(null)
    queueSave(sortTodos([restored, ...itemsRef.current]))
    setSelectedId(restored.id)
  }

  function updateSelected(patch: Partial<TodoItem>) {
    if (!selected) return
    if (patch.remind !== undefined) maybeAskNotifications(patch.remind)
    queueSave(sortTodos(items.map((t) => (t.id === selected.id ? { ...t, ...patch } : t))))
  }

  if (collapsed && !fill) {
    return (
      <aside className="pane todos pane-rail-collapsed">
        <button type="button" className="pane-collapsed-tab" onClick={onToggleCollapsed} title="Show todos">
          Todos
          {items.length > 0 ? ` · ${items.length}` : ''}
        </button>
      </aside>
    )
  }

  return (
    <aside className={`pane todos${fill ? ' todos-fill' : ''}`}>
      <div className="todos-head">
        <div className="todos-head-main">
          <h2>Todos{items.length ? ` · ${items.length}` : ''}</h2>
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
      {busy && items.length === 0 && <p className="muted todos-empty">Loading…</p>}
      {!busy && items.length === 0 && !pendingUndo && (
        <p className="muted todos-empty">Nothing open — add something above.</p>
      )}

      <ul className="todos-list">
        {items.map((t) => (
          <li key={t.id}>
            <div className={`todos-row priority-${t.priority}${selectedId === t.id ? ' on' : ''}`}>
              <input
                type="checkbox"
                checked={false}
                aria-label={`Complete ${t.title}`}
                onChange={() => completeTodo(t.id)}
              />
              <button type="button" className="todos-row-main" onClick={() => setSelectedId(t.id)}>
                <span className="todos-title">{t.title}</span>
                <span className="todos-meta">
                  <span className={`todos-pri pri-${t.priority}`}>{t.priority}</span>
                  {formatDueLabel(t.due) && <span className="todos-due">{formatDueLabel(t.due)}</span>}
                  {t.remind !== 'off' && <span className="todos-remind">remind</span>}
                </span>
              </button>
            </div>
          </li>
        ))}
      </ul>

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

      {selected && (
        <div className="todos-detail">
          <div className="todos-detail-head">
            <strong>Edit</strong>
            <button type="button" className="ghost" onClick={() => setSelectedId(null)}>
              Close
            </button>
          </div>
          <label className="field">
            Title
            <input value={selected.title} onChange={(e) => updateSelected({ title: e.target.value })} />
          </label>
          <label className="field">
            Priority
            <select
              value={selected.priority}
              onChange={(e) => {
                const priority = e.target.value as TodoPriority
                if (priority === 'critical') updateSelected({ priority, remind: 'every:30m' })
                else updateSelected({ priority })
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
              value={dueToLocalInput(selected.due)}
              onChange={(e) =>
                updateSelected({
                  due: e.target.value ? new Date(e.target.value).toISOString() : null,
                })
              }
            />
          </label>
          <label className="field">
            Reminder
            <select
              value={remindSelectValue(selected)}
              onChange={(e) => {
                const v = e.target.value
                if (v === 'off') updateSelected({ remind: 'off' })
                else if (v === 'critical') updateSelected({ priority: 'critical', remind: 'every:30m' })
                else if (v === 'every:30m') updateSelected({ remind: 'every:30m' })
                else if (v === 'every:60m') updateSelected({ remind: 'every:60m' })
                else if (v === 'once-due') {
                  if (selected.due) updateSelected({ remind: `once:${selected.due}` })
                  else updateSelected({ remind: `once:${new Date().toISOString()}` })
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
          <button type="button" className="ghost" onClick={() => completeTodo(selected.id)}>
            Mark done
          </button>
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
