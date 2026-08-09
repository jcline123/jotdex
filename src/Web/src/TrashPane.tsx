import { useCallback, useEffect, useState } from 'react'

export type TrashItem = {
  id: string
  kind: string
  originalRelativePath: string
  title: string
  deletedDay: string
  deletedAtUtc: string
  bytes: number
  hasAssets: boolean
}

type Props = {
  fill?: boolean
  collapsed?: boolean
  onExpand?: () => void
  onCollapse?: () => void
  onRestored?: () => void
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / (1024 * 1024)).toFixed(1)} MB`
}

export function TrashPane({ fill, collapsed, onExpand, onCollapse, onRestored }: Props) {
  const [items, setItems] = useState<TrashItem[]>([])
  const [busy, setBusy] = useState(false)
  const [hint, setHint] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const data = await fetch('/api/trash', { credentials: 'same-origin' }).then((r) => r.json())
      setItems(Array.isArray(data.items) ? data.items : [])
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load trash')
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function restore(id: string, asCopy: boolean) {
    setBusy(true)
    setHint(null)
    try {
      const res = await fetch('/api/trash/restore', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ id, asCopy }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) {
        setError(data.error ?? 'Restore failed')
        return
      }
      setHint(asCopy ? `Restored as copy: ${data.restoredRelativePath}` : `Restored: ${data.restoredRelativePath}`)
      await load()
      onRestored?.()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Restore failed')
    } finally {
      setBusy(false)
    }
  }

  async function remove(id: string, title: string) {
    if (!window.confirm(`Permanently delete “${title}”? This cannot be undone.`)) return
    setBusy(true)
    try {
      const res = await fetch('/api/trash/delete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ id }),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok || data.success === false) {
        setError(data.error ?? 'Delete failed')
        return
      }
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed')
    } finally {
      setBusy(false)
    }
  }

  if (collapsed) {
    return (
      <aside className={`pane trash-pane collapsed ${fill ? 'fill' : ''}`}>
        <button type="button" className="pane-rail" onClick={onExpand} title="Expand Trash">
          <span className="pane-rail-label">Trash</span>
        </button>
      </aside>
    )
  }

  return (
    <aside className={`pane trash-pane ${fill ? 'fill' : ''}`}>
      <div className="pane-head">
        <strong>Trash</strong>
        <div className="pane-head-actions">
          <button type="button" className="ghost" disabled={busy} onClick={() => void load()}>
            Refresh
          </button>
          {onCollapse && (
            <button type="button" className="ghost" onClick={onCollapse} title="Back to notes">
              Close
            </button>
          )}
        </div>
      </div>
      <p className="muted trash-lede">Deleted notes live in app data until restored or emptied.</p>
      {error && <p className="error">{error}</p>}
      {hint && <p className="muted">{hint}</p>}
      <ul className="note-list trash-list">
        {items.length === 0 && <li className="muted">Trash is empty.</li>}
        {items.map((item) => (
          <li key={item.id} className="trash-item">
            <div className="trash-item-main">
              <strong>{item.title}</strong>
              <span className="muted">{item.originalRelativePath}</span>
              <span className="muted">
                {item.deletedDay}
                {item.hasAssets ? ' · assets' : ''} · {formatBytes(item.bytes)}
              </span>
            </div>
            <div className="trash-item-actions">
              <button type="button" className="ghost" disabled={busy} onClick={() => void restore(item.id, false)}>
                Restore
              </button>
              <button type="button" className="ghost" disabled={busy} onClick={() => void restore(item.id, true)}>
                As copy
              </button>
              <button type="button" className="ghost danger-text" disabled={busy} onClick={() => void remove(item.id, item.title)}>
                Delete
              </button>
            </div>
          </li>
        ))}
      </ul>
    </aside>
  )
}
