import { useEffect, useMemo, useState } from 'react'
import { NOTE_TEMPLATES, type NoteTemplate } from './templates'
import { fetchTemplates, instantiateTemplate, type VaultTemplateSummary } from './templateApi'

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

type TemplateChoice =
  | { kind: 'blank' }
  | { kind: 'builtin'; template: NoteTemplate }
  | { kind: 'vault'; template: VaultTemplateSummary }

export function NewNoteModal({ tree, defaultFolder, onClose, onCreated }: Props) {
  const folders = useMemo(() => {
    const list = flattenFolders(tree).sort((a, b) => a.label.localeCompare(b.label))
    return list
  }, [tree])

  const [title, setTitle] = useState('Untitled')
  const [folder, setFolder] = useState(defaultFolder)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [vaultTemplates, setVaultTemplates] = useState<VaultTemplateSummary[]>([])
  const [choice, setChoice] = useState<TemplateChoice>({ kind: 'blank' })

  useEffect(() => {
    void fetchTemplates()
      .then(setVaultTemplates)
      .catch(() => setVaultTemplates([]))
  }, [])

  function applyChoice(next: TemplateChoice) {
    setChoice(next)
    if (next.kind === 'builtin') {
      const suggested =
        typeof next.template.defaultTitle === 'function'
          ? next.template.defaultTitle(new Date().toISOString().slice(0, 10))
          : (next.template.defaultTitle ?? title)
      if (suggested) setTitle(suggested)
    } else if (next.kind === 'vault') {
      setTitle(next.template.title)
    }
  }

  async function save(e: React.FormEvent) {
    e.preventDefault()
    if (!title.trim()) {
      setError('Enter a title.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      if (choice.kind === 'vault') {
        const note = await instantiateTemplate({
          templateId: choice.template.noteId,
          title: title.trim(),
          folder: folder.trim(),
        })
        onCreated(note.id, folder.trim())
        return
      }

      const markdown =
        choice.kind === 'builtin' ? choice.template.body(title.trim()) : undefined
      const res = await fetch('/api/notes', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title: title.trim(), folder: folder.trim(), markdown }),
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
        <p className="muted clip-save-lede">Choose a folder and optional template, then create the note.</p>
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
          <label className="field">
            Template
            <select
              value={
                choice.kind === 'blank'
                  ? 'blank'
                  : choice.kind === 'builtin'
                    ? `builtin:${choice.template.id}`
                    : `vault:${choice.template.noteId}`
              }
              onChange={(e) => {
                const v = e.target.value
                if (v === 'blank') {
                  applyChoice({ kind: 'blank' })
                  return
                }
                if (v.startsWith('builtin:')) {
                  const id = v.slice('builtin:'.length)
                  const t = NOTE_TEMPLATES.find((x) => x.id === id)
                  if (t) applyChoice({ kind: 'builtin', template: t })
                  return
                }
                if (v.startsWith('vault:')) {
                  const id = v.slice('vault:'.length)
                  const t = vaultTemplates.find((x) => x.noteId === id)
                  if (t) applyChoice({ kind: 'vault', template: t })
                }
              }}
            >
              <option value="blank">Blank note</option>
              {vaultTemplates.length > 0 && (
                <optgroup label="Your templates">
                  {vaultTemplates.map((t) => (
                    <option key={t.noteId} value={`vault:${t.noteId}`}>
                      {t.title}
                    </option>
                  ))}
                </optgroup>
              )}
              <optgroup label="Built-in">
                {NOTE_TEMPLATES.filter((t) => t.id !== 'blank').map((t) => (
                  <option key={t.id} value={`builtin:${t.id}`}>
                    {t.name}
                  </option>
                ))}
              </optgroup>
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
