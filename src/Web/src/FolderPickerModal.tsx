import { useMemo, useState } from 'react'

export type FolderPickNode = {
  id: string
  name: string
  relativePath: string
  children: FolderPickNode[]
}

type Props = {
  tree: FolderPickNode | null
  title: string
  lede: string
  confirmLabel: string
  initialPath: string
  /** Folder being moved — itself and descendants cannot be chosen. */
  disablePath?: string
  onClose: () => void
  onPick: (folderPath: string) => void | Promise<void>
}

function pathMatches(path: string, name: string, q: string): boolean {
  if (!q) return true
  const n = q.toLowerCase()
  return path.toLowerCase().includes(n) || name.toLowerCase().includes(n)
}

function filterTree(node: FolderPickNode, q: string): FolderPickNode | null {
  if (!q) return node
  const kids = node.children.map((c) => filterTree(c, q)).filter((c): c is FolderPickNode => c !== null)
  const self = pathMatches(node.relativePath, node.name, q)
  if (!self && kids.length === 0) return null
  return { ...node, children: kids }
}

function isDisabledDest(path: string, disablePath: string | undefined): boolean {
  if (!disablePath) return false
  const d = disablePath.replace(/\\/g, '/')
  const p = path.replace(/\\/g, '/')
  return p === d || p.startsWith(d + '/')
}

function PickerTree({
  node,
  depth,
  picked,
  disablePath,
  onPick,
}: {
  node: FolderPickNode
  depth: number
  picked: string
  disablePath?: string
  onPick: (path: string) => void
}) {
  const [collapsed, setCollapsed] = useState(false)
  const hasKids = node.children.length > 0
  const disabled = isDisabledDest(node.relativePath, disablePath)
  const showKids = hasKids && !collapsed

  return (
    <div className="tree-branch">
      <div
        className={`tree-row${picked === node.relativePath ? ' active' : ''}${disabled ? ' picker-disabled' : ''}`}
        style={{ paddingLeft: `${0.35 + depth * 0.85}rem` }}
      >
        {hasKids && node.relativePath !== '' ? (
          <button
            type="button"
            className="tree-twist"
            aria-label={collapsed ? 'Expand folder' : 'Collapse folder'}
            aria-expanded={!collapsed}
            onClick={(e) => {
              e.stopPropagation()
              setCollapsed((c) => !c)
            }}
          >
            {collapsed ? '▸' : '▾'}
          </button>
        ) : (
          <span className="tree-twist spacer" aria-hidden />
        )}
        <button
          type="button"
          className="tree-item"
          disabled={disabled}
          onClick={() => {
            if (!disabled) onPick(node.relativePath)
          }}
        >
          {node.relativePath === '' ? 'Vault root' : node.name}
        </button>
      </div>
      {showKids &&
        node.children.map((c) => (
          <PickerTree
            key={c.id}
            node={c}
            depth={depth + 1}
            picked={picked}
            disablePath={disablePath}
            onPick={onPick}
          />
        ))}
    </div>
  )
}

export function FolderPickerModal({
  tree,
  title,
  lede,
  confirmLabel,
  initialPath,
  disablePath,
  onClose,
  onPick,
}: Props) {
  const [picked, setPicked] = useState(initialPath.replace(/\\/g, '/'))
  const [query, setQuery] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const shown = useMemo(() => {
    if (!tree) return null
    return filterTree(tree, query.trim())
  }, [tree, query])

  async function confirm() {
    if (isDisabledDest(picked, disablePath)) {
      setError('Choose a different folder.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await onPick(picked)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not move')
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal folder-picker-modal"
        role="dialog"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-head">
          <h2>{title}</h2>
          <button type="button" className="ghost" onClick={onClose}>
            Cancel
          </button>
        </div>
        <p className="muted clip-save-lede">{lede}</p>
        <label className="field">
          Filter folders
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Type to find a folder…"
            autoComplete="off"
          />
        </label>
        <div className="folder-picker-tree" role="listbox" aria-label="Folders">
          {shown ? (
            <PickerTree node={shown} depth={0} picked={picked} disablePath={disablePath} onPick={setPicked} />
          ) : (
            <p className="muted" style={{ padding: '0.65rem 0.85rem' }}>
              No folders match.
            </p>
          )}
        </div>
        <p className="muted">
          Selected: <code>{picked || '(vault root)'}</code>
        </p>
        {error && <p className="err">{error}</p>}
        <div className="modal-actions">
          <button type="button" className="primary" disabled={busy} onClick={() => void confirm()}>
            {busy ? 'Moving…' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
