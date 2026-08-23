import { useEffect, useState } from 'react'
import { fetchSnippets, type SnippetSummary } from './snippetApi'
import { normalizeLanguageId } from './codeLanguages'

type Props = {
  language?: string
  onClose: () => void
  onPick: (snippet: SnippetSummary) => void
}

export function InsertSnippetModal({ language, onClose, onPick }: Props) {
  const [q, setQ] = useState('')
  const [items, setItems] = useState<SnippetSummary[]>([])
  const [busy, setBusy] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setBusy(true)
    void (async () => {
      try {
        const lang = language ? normalizeLanguageId(language) : undefined
        const list = await fetchSnippets(q, lang === 'plaintext' ? undefined : lang)
        if (!cancelled) {
          setItems(list)
          setError(null)
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load snippets')
      } finally {
        if (!cancelled) setBusy(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [q, language])

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div className="modal folder-picker-modal" role="dialog" aria-label="Insert snippet" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h2>Insert snippet</h2>
          <button type="button" className="ghost" onClick={onClose}>
            Cancel
          </button>
        </div>
        <label className="field">
          Search
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Title, trigger, tags, code…" autoFocus />
        </label>
        {error && <p className="banner error">{error}</p>}
        {busy ? (
          <p className="muted">Loading…</p>
        ) : items.length === 0 ? (
          <p className="muted">No snippets found. Save a code block as a snippet first (stored under Snippets/ in your vault).</p>
        ) : (
          <ul className="snippet-pick-list">
            {items.map((s) => (
              <li key={s.noteId}>
                <button type="button" className="snippet-pick-row" onClick={() => onPick(s)}>
                  <strong>{s.title}</strong>
                  <span className="muted">
                    {s.trigger} · {s.language}
                    {s.tags.length > 0 ? ` · ${s.tags.join(', ')}` : ''}
                  </span>
                  {s.description && <span className="snippet-pick-desc">{s.description}</span>}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
