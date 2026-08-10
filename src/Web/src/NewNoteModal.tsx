import { useMemo, useState } from 'react'

type FolderNode = {
  relativePath?: string
  children?: FolderNode[]
}

type Props = {
  tree: FolderNode | null
  defaultFolder: string
  onClose: () => void
  onCreated: (noteId: string, folderPath: string) => void
}

function flattenFolders(node: FolderNode | null | undefined, acc: { path: string; label: string }[] = []) {
  if (!node) return acc
  const p = (node.relativePath || '').replace(/\\/g, '/')
  if (p) acc.push({ path: p, label: p })
  for (const c of node.children ?? []) flattenFolders(c, acc)
  return acc
}

export function NewNoteModal({ tree, defaultFolder, onClose, onCreated }: Props) {
  const folders = useMemo(() => {
    const list = flattenFolders(tree).sort((a, b) => a.label.localeCompare(b.label))
    return list
  }, [tree])

  const [title, setTitle] = useState('Untitled')
  const [folder, setFolder] = useState(defaultFolder)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save(e: React.FormEvent) {
    e.preventDefault()
    if (!title.trim()) {
      setError('Enter a title.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const res = await fetch('/api/notes', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title: title.trim(), folder: folder.trim() }),
      })
      const data = await res.json()
      if (!res.ok) throw new Error(data.error ?? 'Could not create note')
      onCreated(data.id as string, folder.trim())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create note')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal new-note-modal"
        role="dialog"
        aria-label="New note"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-head">
          <h2>New note</h2>
          <button type="button" className="ghost" onClick={onClose}>
            Cancel
          </button>
        </div>
        <p className="muted clip-save-lede">Choose a folder, then create the note.</p>
        <form className="auth-form" onSubmit={(e) => void save(e)}>
          <label className="field">
            Title
            <input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              autoFocus
              onFocus={(e) => e.currentTarget.select()}
            />
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
          {error && <p className="error">{error}</p>}
          <div className="modal-actions">
            <button type="submit" className="primary" disabled={busy}>
              {busy ? 'Creating…' : 'Create note'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

export function folderRailShortLabel(folderPath: string): string {
  if (!folderPath.trim()) return 'All notes'
  const parts = folderPath.replace(/\\/g, '/').split('/').filter(Boolean)
  return parts[parts.length - 1] || folderPath
}
