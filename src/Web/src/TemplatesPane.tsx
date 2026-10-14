import { useCallback, useEffect, useState } from 'react'
import {
  createTemplate,
  deleteTemplate,
  fetchTemplates,
  type VaultTemplateSummary,
} from './templateApi'

type Props = {
  fill?: boolean
  activeTemplateId?: string | null
  onCollapse?: () => void
  onChanged?: () => void
  onOpenTemplate: (template: VaultTemplateSummary) => void
  onUseTemplate: (template: VaultTemplateSummary) => void
  onInsertTemplate: (template: VaultTemplateSummary) => void
  canInsert?: boolean
}

export function TemplatesPane({
  fill,
  activeTemplateId,
  onCollapse,
  onChanged,
  onOpenTemplate,
  onUseTemplate,
  onInsertTemplate,
  canInsert = false,
}: Props) {
  const [items, setItems] = useState<VaultTemplateSummary[]>([])
  const [query, setQuery] = useState('')
  const [busy, setBusy] = useState(false)
  const [hint, setHint] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const list = await fetchTemplates(query.trim() || undefined)
      setItems(list)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load templates')
    }
  }, [query])

  useEffect(() => {
    void load()
  }, [load])

  async function createBlank() {
    const title = window.prompt('New template name', 'Untitled template')
    if (!title?.trim()) return
    setBusy(true)
    setError(null)
    try {
      const t = await createTemplate({
        title: title.trim(),
        markdown: `# ${title.trim()}\n\n`,
      })
      setHint(`Created “${t.title}”.`)
      await load()
      onChanged?.()
      onOpenTemplate(t)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create template')
    } finally {
      setBusy(false)
    }
  }

  async function removeTemplate(t: VaultTemplateSummary, e: React.MouseEvent) {
    e.stopPropagation()
    if (!window.confirm(`Move template “${t.title}” to trash?`)) return
    setBusy(true)
    setError(null)
    try {
      await deleteTemplate(t.noteId)
      setHint(`Moved “${t.title}” to trash.`)
      await load()
      onChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not delete template')
    } finally {
      setBusy(false)
    }
  }

  return (
    <aside className={`pane snippets-pane templates-pane ${fill ? 'fill' : ''}`}>
      <div className="snippets-pane-head">
        <strong className="snippets-pane-title">Templates</strong>
        <div className="snippets-pane-actions">
          <button type="button" className="ghost" disabled={busy} onClick={() => void createBlank()}>
            New
          </button>
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
      <p className="muted snippets-lede">
        Reusable layouts in <code>Templates/</code>. <strong>Use</strong> creates a new note; <strong>Insert</strong>{' '}
        pastes into the open note at the cursor (also: note header → <strong>Insert template</strong>). Editing a
        template does not change notes already created from it.
      </p>
      <div className="snippets-search">
        <label className="sr-only" htmlFor="templates-search-input">
          Search templates
        </label>
        <input
          id="templates-search-input"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search templates…"
        />
      </div>
      {error && <p className="banner error">{error}</p>}
      {hint && <p className="muted snippets-hint">{hint}</p>}
      <ul className="snippets-list">
        {items.length === 0 && (
          <li className="muted snippets-empty">No templates yet — save the current note as a template, or create one.</li>
        )}
        {items.map((t) => (
          <li key={t.noteId} className={`snippet-list-item${activeTemplateId === t.noteId ? ' is-active' : ''}`}>
            <button type="button" className="snippet-row" onClick={() => onOpenTemplate(t)}>
              <span className="snippet-row-title">{t.title}</span>
              <span className="snippet-row-meta">
                {t.description ? <span className="snippet-lang">{t.description}</span> : null}
                {t.hasAttachments ? <span className="snippet-chip">files</span> : null}
              </span>
            </button>
            <div className="template-row-actions">
              <button
                type="button"
                className="ghost"
                title="Create a new note from this template"
                disabled={busy}
                onClick={(e) => {
                  e.stopPropagation()
                  onUseTemplate(t)
                }}
              >
                Use
              </button>
              <button
                type="button"
                className="ghost"
                title={
                  canInsert
                    ? 'Insert into the current note at the cursor'
                    : 'Open a note first to insert a template'
                }
                disabled={busy || !canInsert}
                onClick={(e) => {
                  e.stopPropagation()
                  onInsertTemplate(t)
                }}
              >
                Insert
              </button>
              <button
                type="button"
                className="ghost snippet-list-delete"
                title="Move template to trash"
                disabled={busy}
                onClick={(e) => void removeTemplate(t, e)}
              >
                Delete
              </button>
            </div>
          </li>
        ))}
      </ul>
    </aside>
  )
}
