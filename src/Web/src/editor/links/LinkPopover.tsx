import type { Editor } from '@tiptap/core'
import { isSafeHref, looksLikeBareUrl, hostnameLabel } from './linkSchemes'
import { insertMarkdown } from '../operations/contentInsertion'

type NoteHit = { id: string; title: string; relativePath: string; folderPath: string }

type Props = {
  editor: Editor
  href: string
  onHref: (v: string) => void
  notes: NoteHit[]
  relativeFor: (path: string) => string
  onClose: () => void
}

export function LinkPopover({ editor, href, onHref, notes, relativeFor, onClose }: Props) {
  const q = href.trim().toLowerCase()
  const hits = notes
    .filter((n) => !q || n.title.toLowerCase().includes(q) || n.relativePath.toLowerCase().includes(q))
    .slice(0, 8)

  const applyLink = (url: string, label?: string) => {
    if (!isSafeHref(url)) return
    const { empty } = editor.state.selection
    if (empty) {
      insertMarkdown(editor, `[${label || url}](${url})`)
    } else {
      editor.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
    }
    onClose()
  }

  const applyCard = (url: string) => {
    if (!isSafeHref(url)) return
    insertMarkdown(editor, `<!-- jotdex-link-card -->\n[${hostnameLabel(url)}](${url})\n`)
    onClose()
  }

  return (
    <div className="jotdex-link-popover" role="dialog" aria-label="Link">
      <input
        autoFocus
        value={href}
        placeholder="URL or note title"
        aria-label="Link URL"
        onChange={(e) => onHref(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            if (looksLikeBareUrl(href) && editor.state.selection.empty) applyCard(href.trim())
            else applyLink(href.trim())
          }
          if (e.key === 'Escape') onClose()
        }}
      />
      <div className="jotdex-link-actions">
        <button type="button" onClick={() => applyLink(href.trim())} disabled={!isSafeHref(href)}>
          Link
        </button>
        <button type="button" onClick={() => applyCard(href.trim())} disabled={!isSafeHref(href)}>
          Card
        </button>
        <button
          type="button"
          onClick={() => {
            editor.chain().focus().unsetLink().run()
            onClose()
          }}
        >
          Remove
        </button>
      </div>
      {hits.length > 0 && (
        <ul className="jotdex-link-notes">
          {hits.map((n) => (
            <li key={n.id}>
              <button type="button" onClick={() => applyLink(relativeFor(n.relativePath), n.title)}>
                {n.title}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
