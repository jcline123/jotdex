import { useMemo, useState } from 'react'
import { createSnippet } from './snippetApi'

type FolderNode = {
  relativePath?: string
  children?: FolderNode[]
}

type Props = {
  tree: FolderNode | null
  defaultFolder: string
  language: string
  code: string
  onClose: () => void
  onSaved?: () => void
}

function flattenFolders(node: FolderNode | null | undefined, acc: { path: string; label: string }[] = []) {
  if (!node) return acc
  const p = (node.relativePath || '').replace(/\\/g, '/')
  if (p) acc.push({ path: p, label: p })
  for (const c of node.children ?? []) flattenFolders(c, acc)
  return acc
}

export function SaveAsSnippetModal({ tree, defaultFolder, language, code, onClose, onSaved }: Props) {
  const folders = useMemo(() => flattenFolders(tree).sort((a, b) => a.label.localeCompare(b.label)), [tree])
  const [title, setTitle] = useState('Snippet')
  const [trigger, setTrigger] = useState('')
  const [folder, setFolder] = useState(defaultFolder)
  const [description, setDescription] = useState('')
  const [tags, setTags] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await createSnippet({
        title: title.trim(),
        trigger: trigger.trim(),
        language,
        code,
        folder: folder.trim(),
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
        <p className="muted clip-save-lede">Creates a normal note in your vault with snippet front matter.</p>
        <form className="auth-form" onSubmit={(e) => void save(e)}>
          <label className="field">
            Title
            <input value={title} onChange={(e) => setTitle(e.target.value)} autoFocus required />
          </label>
          <label className="field">
            Trigger (for search / Ctrl+Space)
            <input value={trigger} onChange={(e) => setTrigger(e.target.value)} placeholder="restart-spooler" />
          </label>
          <label className="field">
            Folder
            <select value={folder} onChange={(e) => setFolder(e.target.value)}>
              <option value="">Vault root</option>
              {folders.map((f) => (
                <option key={f.path} value={f.path}>
                  {f.label}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            Short description (optional)
            <input value={description} onChange={(e) => setDescription(e.target.value)} />
          </label>
          <label className="field">
            Tags (comma-separated)
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
