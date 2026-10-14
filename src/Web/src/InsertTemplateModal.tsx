import { useEffect, useState } from 'react'
import { fetchTemplates, type VaultTemplateSummary } from './templateApi'

type Props = {
  onClose: () => void
  onPick: (template: VaultTemplateSummary) => void | Promise<void>
}

/** Pick a vault template to insert at the cursor in the current note. */
export function InsertTemplateModal({ onClose, onPick }: Props) {
  const [items, setItems] = useState<VaultTemplateSummary[]>([])
  const [query, setQuery] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void fetchTemplates()
      .then(setItems)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not load templates'))
  }, [])

  const filtered = items.filter((t) => {
    const q = query.trim().toLowerCase()
    if (!q) return true
    return (
      t.title.toLowerCase().includes(q) ||
      (t.description?.toLowerCase().includes(q) ?? false)
    )
  })

  async function pick(t: VaultTemplateSummary) {
    setBusy(true)
    setError(null)
    try {
      await onPick(t)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not insert template')
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal insert-template-modal"
        role="dialog"
        aria-label="Insert template"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-head">
          <h2>Insert template</h2>
          <button type="button" className="ghost" onClick={onClose}>
            Cancel
          </button>
        </div>
        <p className="muted clip-save-lede">
          Inserts at the cursor (or asks to append if the editor is not focused). Your note stays
          independent — undo with Ctrl+Z.
        </p>
        <label className="field">
          Search
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Filter templates…"
            autoFocus
          />
        </label>
        {error && <p className="error">{error}</p>}
        <ul className="insert-template-list">
          {filtered.length === 0 && (
            <li className="muted">
              {items.length === 0
                ? 'No templates yet — save a note as a template, or open Templates in the folders rail.'
                : 'No matches.'}
            </li>
          )}
          {filtered.map((t) => (
            <li key={t.noteId}>
              <button
                type="button"
                className="insert-template-row"
                disabled={busy}
                onClick={() => void pick(t)}
              >
                <strong>{t.title}</strong>
                {t.description ? <span>{t.description}</span> : null}
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
