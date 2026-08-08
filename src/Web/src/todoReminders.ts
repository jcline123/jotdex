import type { TodoItem } from './todosMarkdown'
import { parseRemindEveryMinutes, parseRemindOnce, todoIsDueForReminder } from './todosMarkdown'

const STORAGE_KEY = 'jotdex.todoRemindState'
const PERMISSION_ASKED = 'jotdex.todoNotifyAsked'

type RemindState = Record<string, { lastFiredUtc: number; onceKey?: string }>

function loadState(): RemindState {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return {}
    return JSON.parse(raw) as RemindState
  } catch {
    return {}
  }
}

function saveState(state: RemindState) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state))
  } catch {
    /* ignore */
  }
}

export type NotifyPermission = NotificationPermission | 'unsupported'

export function getNotificationPermission(): NotifyPermission {
  if (typeof Notification === 'undefined') return 'unsupported'
  return Notification.permission
}

/**
 * Ask the browser for notification permission.
 * - force: Settings / explicit user click — always call requestPermission when still "default"
 * - soft (default): at most once automatically (e.g. first reminder), unless force
 */
export async function promptTodoNotifications(opts?: { force?: boolean }): Promise<NotifyPermission> {
  if (typeof Notification === 'undefined') return 'unsupported'
  if (Notification.permission === 'granted') return 'granted'
  if (Notification.permission === 'denied') return 'denied'

  const force = opts?.force === true
  if (!force) {
    try {
      if (localStorage.getItem(PERMISSION_ASKED) === '1') return Notification.permission
    } catch {
      /* ignore */
    }
  }

  try {
    localStorage.setItem(PERMISSION_ASKED, '1')
  } catch {
    /* ignore */
  }

  try {
    await Notification.requestPermission()
  } catch {
    /* ignore */
  }
  return Notification.permission
}

async function ensurePermission(): Promise<boolean> {
  const p = await promptTodoNotifications({ force: false })
  return p === 'granted'
}

function fire(todo: TodoItem, body: string) {
  if (typeof Notification === 'undefined' || Notification.permission !== 'granted') return
  try {
    // `tag` replaces prior notification for the same todo.
    new Notification(todo.title, {
      body,
      tag: `jotdex-todo-${todo.id}`,
    } as NotificationOptions)
  } catch {
    /* ignore */
  }
}

/**
 * While the tab is open: fire due/once/every reminders.
 * Catch-up: at most one notification per todo when returning after being away.
 */
export function startTodoReminderLoop(getTodos: () => TodoItem[]): () => void {
  let state = loadState()

  const tick = async (catchUp: boolean) => {
    if (document.visibilityState === 'hidden') return
    const todos = getTodos()
    const now = Date.now()
    let changed = false

    for (const todo of todos) {
      if (!todo.remind || todo.remind === 'off') continue
      if (!todoIsDueForReminder(todo, now) && parseRemindEveryMinutes(todo.remind) == null) continue

      const once = parseRemindOnce(todo.remind)
      const everyMin = parseRemindEveryMinutes(todo.remind)
      const prev = state[todo.id]

      if (once != null) {
        if (now < once) continue
        const onceKey = String(once)
        if (prev?.onceKey === onceKey) continue
        if (!(await ensurePermission())) return
        fire(todo, catchUp ? 'Reminder was due while you were away' : 'Reminder')
        state[todo.id] = { lastFiredUtc: now, onceKey }
        changed = true
        continue
      }

      if (everyMin != null) {
        const intervalMs = everyMin * 60_000
        const last = prev?.lastFiredUtc
        // First sighting: arm the clock without notifying (avoids spam on load).
        if (last == null) {
          state[todo.id] = { lastFiredUtc: now }
          changed = true
          continue
        }
        // One alert per due interval — including catch-up after being away.
        if (now - last >= intervalMs) {
          if (!(await ensurePermission())) return
          fire(
            todo,
            catchUp
              ? `Repeating reminder (${everyMin}m) — due while you were away`
              : `Repeating every ${everyMin}m until done`,
          )
          state[todo.id] = { lastFiredUtc: now }
          changed = true
        }
      }
    }

    // Drop state for todos that no longer exist
    for (const id of Object.keys(state)) {
      if (!todos.some((t) => t.id === id)) {
        delete state[id]
        changed = true
      }
    }

    if (changed) {
      saveState(state)
      state = loadState()
    }
  }

  void tick(true)
  const onVis = () => {
    if (document.visibilityState === 'visible') void tick(true)
  }
  document.addEventListener('visibilitychange', onVis)
  const interval = window.setInterval(() => void tick(false), 30_000)

  return () => {
    document.removeEventListener('visibilitychange', onVis)
    window.clearInterval(interval)
  }
}
