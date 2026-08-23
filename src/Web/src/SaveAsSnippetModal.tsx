import { useRef, useState } from 'react'
import { createSnippet } from './snippetApi'

type Props = {
  language: string
  code: string
  onClose: () => void
  onSaved?: () => void
}

export function SaveAsSnippetModal({ language, code, onClose, onSaved }: Props) {
  const [title, setTitle] = useState('Snippet')
  const [trigger, setTrigger] = useState('')
  const [description, setDescription] = useState('')
  const [tags, setTags] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const savingRef = useRef(false)

  async function save(e: React.FormEvent) {
    e.preventDefault()
    if (savingRef.current || busy) return
    savingRef.current = true
    setBusy(true)
    setError(null)
    try {
      await createSnippet({
        title: title.trim(),
        trigger: trigger.trim(),
        language,
        code,
        folder: 'Snippets',
        description: description.trim() || undefined,
        tags: tags
          .split(',')
          .map((t) => t.trim())
          .filter(Boolean),
      })
      onSaved?.()
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save snippet')
    } finally {
      savingRef.current = false
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div className="modal new-note-modal" role="dialog" aria-label="Save as snippet" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h2>Save as snippet</h2>
          <button type="button" className="ghost" onClick={onClose}>
            Cancel
          </button>
        </div>
        <p className="muted clip-save-lede">
          Saves into a reserved <code>Snippets</code> folder in your vault. Snippets stay out of the notes list — use{' '}
          <strong>Insert</strong> or Ctrl+Space in Edit to reuse them.
        </p>
        <form className="auth-form" onSubmit={(e) => void save(e)}>
          <label className="field">
            Name
            <span className="field-hint">Shown in the Insert list (e.g. Restart print spooler).</span>
            <input value={title} onChange={(e) => setTitle(e.target.value)} autoFocus required />
          </label>
          <label className="field">
            Shortcut
            <span className="field-hint">
              Short keyword you type in the Edit dialog, then Ctrl+Space, to insert this snippet (e.g.{' '}
              <code>restart-spooler</code>). Leave blank to derive one from the name.
            </span>
            <input
              value={trigger}
              onChange={(e) => setTrigger(e.target.value)}
              placeholder="restart-spooler"
              autoCapitalize="off"
              autoCorrect="off"
              spellCheck={false}
            />
          </label>
          <label className="field">
            Short description (optional)
            <span className="field-hint">One line explaining when to use this snippet.</span>
            <input value={description} onChange={(e) => setDescription(e.target.value)} />
          </label>
          <label className="field">
            Tags (optional)
            <span className="field-hint">Comma-separated labels for filtering in Insert search (e.g. windows, services).</span>
            <input value={tags} onChange={(e) => setTags(e.target.value)} placeholder="windows, services" />
          </label>
          <p className="muted">Language: {language}</p>
          {error && <p className="banner error">{error}</p>}
          <div className="modal-actions">
            <button type="submit" disabled={busy}>
              {busy ? 'Saving…' : 'Save snippet'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
