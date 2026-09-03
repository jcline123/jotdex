/** Vault-backed todo lines in Todos.md */

export type TodoPriority = 'low' | 'normal' | 'high' | 'critical'

/** off | once:ISO-8601 | every:30m | every:1h */
export type TodoRemind = string

export type TodoItem = {
  id: string
  title: string
  priority: TodoPriority
  due: string | null
  remind: TodoRemind
  /** ISO-8601 instant when the item was added from the rail; omitted on older lines. */
  added?: string | null
}

const TODO_RE =
  /^- \[([ xX])\]\s+(.*?)\s*<!--\s*jotdex-todo\s+([^>]*)-->\s*$/

export function newTodoId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID()
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

function parseAttrs(raw: string): Record<string, string> {
  const out: Record<string, string> = {}
  const re = /(\w+)="([^"]*)"/g
  let m: RegExpExecArray | null
  while ((m = re.exec(raw))) {
    out[m[1]!] = m[2]!
  }
  return out
}

const PRIORITY_RANK: Record<TodoPriority, number> = { critical: 0, high: 1, normal: 2, low: 3 }

export function normalizeTodoPriority(p?: string | null): TodoPriority {
  if (p === 'low' || p === 'normal' || p === 'high' || p === 'critical') return p
  return 'normal'
}

function addedMs(raw?: string | null): number {
  if (!raw) return 0
  const t = Date.parse(raw)
  return Number.isFinite(t) ? t : 0
}

function dueMs(raw?: string | null): number {
  if (!raw) return Number.POSITIVE_INFINITY
  const t = Date.parse(raw)
  return Number.isFinite(t) ? t : Number.POSITIVE_INFINITY
}

export function parseTodosMarkdown(markdown: string): TodoItem[] {
  const items: TodoItem[] = []
  for (const line of markdown.split(/\r?\n/)) {
    const m = TODO_RE.exec(line.trimEnd())
    if (!m) continue
    const done = m[1] === 'x' || m[1] === 'X'
    if (done) continue // done tasks are not shown / ignored on load
    const title = (m[2] ?? '').trim()
    if (!title) continue
    const attrs = parseAttrs(m[3] ?? '')
    const priority = normalizeTodoPriority(attrs.priority)
    items.push({
      id: attrs.id || newTodoId(),
      title,
      priority,
      due: attrs.due || null,
      remind: attrs.remind || 'off',
      added: attrs.added || null,
    })
  }
  return items
}

export function sortTodos(items: TodoItem[]): TodoItem[] {
  return [...items].sort((a, b) => compareTodoOrder(a, b))
}

function compareTodoOrder(
  a: { priority: TodoPriority; due?: string | null; added?: string | null; title: string },
  b: { priority: TodoPriority; due?: string | null; added?: string | null; title: string },
): number {
  const pr = PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority]
  if (pr !== 0) return pr
  const added = addedMs(b.added) - addedMs(a.added)
  if (added !== 0) return added
  const due = dueMs(a.due) - dueMs(b.due)
  if (due !== 0) return due
  return a.title.localeCompare(b.title)
}

/** Note-backed open tasks as the rail receives them from /api/tasks. */
export type VaultTaskLike = {
  id: string
  text: string
  due?: string | null
  priority?: string | null
  remind?: string | null
  added?: string | null
  noteId: string
  noteTitle: string
}

export type RailTodo =
  | { kind: 'local'; item: TodoItem }
  | { kind: 'vault'; task: VaultTaskLike }

function railPriority(row: RailTodo): TodoPriority {
  return row.kind === 'local' ? row.item.priority : normalizeTodoPriority(row.task.priority)
}

function railDue(row: RailTodo): string | null {
  return row.kind === 'local' ? row.item.due : row.task.due ?? null
}

function railAdded(row: RailTodo): string | null {
  return row.kind === 'local' ? row.item.added ?? null : row.task.added ?? null
}

function railTitle(row: RailTodo): string {
  return row.kind === 'local' ? row.item.title : row.task.text
}

export function sortRailTodos(rows: RailTodo[]): RailTodo[] {
  return [...rows].sort((a, b) =>
    compareTodoOrder(
      { priority: railPriority(a), due: railDue(a), added: railAdded(a), title: railTitle(a) },
      { priority: railPriority(b), due: railDue(b), added: railAdded(b), title: railTitle(b) },
    ),
  )
}

export function mergeRailTodos(items: TodoItem[], vault: VaultTaskLike[]): RailTodo[] {
  return sortRailTodos([
    ...items.map((item) => ({ kind: 'local' as const, item })),
    ...vault.map((task) => ({ kind: 'vault' as const, task })),
  ])
}

function escapeTitle(title: string): string {
  return title.replace(/\s+/g, ' ').trim()
}

export function serializeTodosMarkdown(frontMatterNote: string, items: TodoItem[]): string {
  // Keep existing YAML front matter from the note; rebuild body from open items only.
  const split = splitFm(frontMatterNote)
  const lines = sortTodos(items).map((t) => {
    const due = t.due ? ` due="${t.due}"` : ''
    const remind = t.remind && t.remind !== 'off' ? ` remind="${t.remind}"` : ' remind="off"'
    const added = t.added ? ` added="${t.added}"` : ''
    return `- [ ] ${escapeTitle(t.title)} <!-- jotdex-todo id="${t.id}" priority="${t.priority}"${due}${remind}${added} -->`
  })
  const body = lines.length ? lines.join('\n') + '\n' : ''
  if (split.fm) return `${split.fm}\n\n${body}`
  return body
}

function splitFm(markdown: string): { fm: string; body: string } {
  if (!markdown.startsWith('---')) return { fm: '', body: markdown }
  const end = markdown.indexOf('\n---', 3)
  if (end < 0) return { fm: '', body: markdown }
  const close = end + 4
  const fm = markdown.slice(0, close).trimEnd()
  const body = markdown.slice(close).replace(/^\r?\n/, '')
  return { fm, body }
}

export function parseRemindEveryMinutes(remind: string): number | null {
  const m = /^every:(\d+)(m|h)$/i.exec(remind.trim())
  if (!m) return null
  const n = Number(m[1])
  if (!Number.isFinite(n) || n <= 0) return null
  return m[2]!.toLowerCase() === 'h' ? n * 60 : n
}

export function parseRemindOnce(remind: string): number | null {
  if (!remind.startsWith('once:')) return null
  const t = Date.parse(remind.slice(5))
  return Number.isFinite(t) ? t : null
}

export function todoIsDueForReminder(todo: TodoItem, now = Date.now()): boolean {
  if (!todo.remind || todo.remind === 'off') return false
  const once = parseRemindOnce(todo.remind)
  if (once != null) return now >= once
  const every = parseRemindEveryMinutes(todo.remind)
  if (every != null) return true // cadence handled by last-fired clock
  return false
}

export function formatDueLabel(due: string | null): string | null {
  if (!due) return null
  const t = Date.parse(due)
  if (!Number.isFinite(t)) return due
  const d = new Date(t)
  const now = new Date()
  const sameDay =
    d.getFullYear() === now.getFullYear() &&
    d.getMonth() === now.getMonth() &&
    d.getDate() === now.getDate()
  if (sameDay) {
    return d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
  }
  return d.toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
}
