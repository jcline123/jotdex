import { NodeViewWrapper } from '@tiptap/react'
import type { NodeViewProps } from '@tiptap/react'
import { useState } from 'react'
import { displayUrlForSrc, getAttachmentResolverState } from './editor/assets/AttachmentResolver'

/** TipTap image with select chrome, broken-state UI, and one-click Remove. */
export function ImageView({ node, selected, deleteNode, editor }: NodeViewProps) {
  const canonical = String(node.attrs.src ?? '')
  const src = displayUrlForSrc(getAttachmentResolverState(editor), canonical)
  const alt = String(node.attrs.alt ?? '')
  const title = String(node.attrs.title ?? '')
  const [broken, setBroken] = useState(false)
  const label = alt || title || fileNameFromSrc(canonical) || 'Image'

  return (
    <NodeViewWrapper
      className={`note-image${selected ? ' is-selected' : ''}${broken ? ' is-broken' : ''}`}
      data-drag-handle
    >
      <div className="note-image-frame" contentEditable={false}>
        {!broken ? (
          <img src={src} alt={alt} title={title || alt} draggable={false} onError={() => setBroken(true)} />
        ) : (
          <div className="note-image-broken" role="img" aria-label={`Broken image: ${label}`}>
            <span className="note-image-broken-icon" aria-hidden>
              ▢
            </span>
            <span className="note-image-broken-label">{label}</span>
            <span className="note-image-broken-hint">Missing or broken — click Remove</span>
          </div>
        )}
        {editor.isEditable && (
          <div className="note-image-actions">
            <button
              type="button"
              className="note-image-remove"
              title="Remove image"
              onClick={(e) => {
                e.preventDefault()
                e.stopPropagation()
                deleteNode()
              }}
            >
              Remove
            </button>
          </div>
        )}
      </div>
    </NodeViewWrapper>
  )
}

function fileNameFromSrc(src: string): string {
  try {
    const path = src.split('?')[0] ?? src
    const part = path.split('/').pop() ?? ''
    return decodeURIComponent(part).replace(/\+/g, ' ')
  } catch {
    return ''
  }
}
