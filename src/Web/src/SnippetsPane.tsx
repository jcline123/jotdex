import { useCallback, useEffect, useState } from 'react'
import { deleteSnippet, fetchSnippets, type SnippetSummary } from './snippetApi'

type Props = {
  fill?: boolean
  activeSnippetId?: string | null
  onCollapse?: () => void
  onChanged?: () => void
  onOpenSnippet: (snippet: SnippetSummary) => void
}

export function SnippetsPane({ fill, activeSnippetId, onCollapse, onChanged, onOpenSnippet }: Props) {
  const [items, setItems] = useState<SnippetSummary[]>([])
  const [query, setQuery] = useState('')
  const [busy, setBusy] = useState(false)
  const [hint, setHint] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const list = await fetchSnippets(query.trim() || undefined)
      setItems(list)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load snippets')
    }
  }, [query])

  useEffect(() => {
    void load()
  }, [load])

  async function removeSnippet(s: SnippetSummary, e: React.MouseEvent) {
    e.stopPropagation()
    if (!window.confirm(`Move “${s.title}” to trash?`)) return
    setBusy(true)
    setError(null)
    try {
      await deleteSnippet(s.noteId)
      setHint(`Moved “${s.title}” to trash.`)
      await load()
      onChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not delete snippet')
    } finally {
      setBusy(false)
    }
  }

  return (
    <aside className={`pane snippets-pane ${fill ? 'fill' : ''}`}>
      <div className="snippets-pane-head">
        <strong className="snippets-pane-title">Snippets</strong>
        <div className="snippets-pane-actions">
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
      <p className="muted snippets-lede">Click to edit in the note pane. Stored in <code>Snippets/</code>.</p>
      <div className="snippets-search">
        <label className="sr-only" htmlFor="snippets-search-input">
          Search snippets
        </label>
        <input
          id="snippets-search-input"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search name, shortcut, tags…"
        />
      </div>
      {error && <p className="banner error">{error}</p>}
      {hint && <p className="muted snippets-hint">{hint}</p>}
      <ul className="snippets-list">
        {items.length === 0 && <li className="muted snippets-empty">No snippets yet — save one from a code box.</li>}
        {items.map((s) => (
          <li key={s.noteId} className={`snippet-list-item${activeSnippetId === s.noteId ? ' is-active' : ''}`}>
            <button type="button" className="snippet-row" onClick={() => onOpenSnippet(s)}>
              <span className="snippet-row-title">{s.title}</span>
              <span className="snippet-row-meta">
                <span className="snippet-chip">{s.trigger || '—'}</span>
                <span className="snippet-lang">{s.language}</span>
              </span>
            </button>
            <button
              type="button"
              className="ghost snippet-list-delete"
              title="Move snippet to trash"
              disabled={busy}
              onClick={(e) => void removeSnippet(s, e)}
            >
              Delete
            </button>
          </li>
        ))}
      </ul>
    </aside>
  )
}
