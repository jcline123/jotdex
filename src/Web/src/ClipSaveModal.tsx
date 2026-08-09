import { useEffect, useState } from 'react'
import type { ClipPayload } from './jotdexBookmarklet'
import { loadClipDefaultFolder, saveClipDefaultFolder } from './jotdexBookmarklet'

type FolderNode = {
  relativePath?: string
  children?: FolderNode[]
}

type Props = {
  initial: ClipPayload
  onClose: () => void
  onSaved: (noteId: string) => void
}

function flattenFolders(node: FolderNode | null | undefined, acc: { path: string; label: string }[] = []) {
  if (!node) return acc
  const p = (node.relativePath || '').replace(/\\/g, '/')
  if (p) acc.push({ path: p, label: p })
  for (const c of node.children ?? []) flattenFolders(c, acc)
  return acc
}

export function ClipSaveModal({ initial, onClose, onSaved }: Props) {
  const [title, setTitle] = useState(initial.title?.trim() || 'Untitled')
  const [text, setText] = useState(initial.text?.trim() || '')
  const [sourceUrl, setSourceUrl] = useState(initial.url?.trim() || '')
  const [folder, setFolder] = useState(() => loadClipDefaultFolder())
  const [folders, setFolders] = useState<{ path: string; label: string }[]>([{ path: 'Inbox', label: 'Inbox' }])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void fetch('/api/tree', { credentials: 'same-origin' })
      .then((r) => r.json())
      .then((tree: FolderNode) => {
        const list = flattenFolders(tree).sort((a, b) => a.label.localeCompare(b.label))
        if (!list.some((f) => f.path.toLowerCase() === 'inbox')) list.unshift({ path: 'Inbox', label: 'Inbox' })
        setFolders(list)
      })
      .catch(() => {
        /* keep Inbox */
      })
  }, [])

  async function save(e: React.FormEvent) {
    e.preventDefault()
    if (!sourceUrl.trim() && !text.trim()) {
      setError('Add a URL or some note text.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const res = await fetch('/api/clip', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title: title.trim() || undefined,
          text: text.trim() || undefined,
          sourceUrl: sourceUrl.trim() || undefined,
          folder: folder.trim() || 'Inbox',
        }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) throw new Error(data.error ?? 'Could not save')
      saveClipDefaultFolder(folder.trim() || 'Inbox')
      onSaved(data.noteId as string)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal clip-save-modal"
        role="dialog"
        aria-label="Save web clip"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-head">
          <h2>Save web clip</h2>
          <button type="button" className="ghost" onClick={onClose}>
            Cancel
          </button>
        </div>
        <p className="muted clip-save-lede">Creates a new note from this page. Choose a folder, then save.</p>
        <form className="auth-form" onSubmit={(e) => void save(e)}>
          <label className="field">
            Title
            <input value={title} onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label className="field">
            Source URL
            <input value={sourceUrl} onChange={(e) => setSourceUrl(e.target.value)} />
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
            Selection / notes
            <textarea value={text} onChange={(e) => setText(e.target.value)} rows={8} />
          </label>
          {error && <p className="error">{error}</p>}
          <div className="modal-actions">
            <button type="submit" className="primary" disabled={busy}>
              {busy ? 'Saving…' : 'Save as new note'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
