import { NodeViewWrapper } from '@tiptap/react'
import type { NodeViewProps } from '@tiptap/react'
import { useEffect, useState } from 'react'
import { displayUrlForSrc, getAttachmentResolverState } from './editor/assets/AttachmentResolver'

function isResolvedDisplaySrc(src: string): boolean {
  return /^(?:\/api\/attachments\/|blob:|data:|https?:)/i.test(src)
}

/** TipTap image with select chrome, inspector, and one-click Remove. */
export function ImageView({ node, selected, deleteNode, editor, updateAttributes }: NodeViewProps) {
  const canonical = String(node.attrs.src ?? '')
  const src = displayUrlForSrc(getAttachmentResolverState(editor), canonical)
  const alt = String(node.attrs.alt ?? '')
  const title = String(node.attrs.title ?? '')
  const caption = String(node.attrs.caption ?? '')
  const width = node.attrs.width != null && node.attrs.width !== '' ? String(node.attrs.width) : ''
  const align = String(node.attrs.align ?? '')
  const lightbox = Boolean(node.attrs.lightbox)
  const [broken, setBroken] = useState(false)
  const [lightOn, setLightOn] = useState(false)
  const label = alt || title || fileNameFromSrc(canonical) || 'Image'
  const runtime = /^(blob:|data:)/i.test(canonical)

  useEffect(() => {
    setBroken(false)
  }, [src])

  return (
    <NodeViewWrapper
      className={`note-image${selected ? ' is-selected' : ''}${broken ? ' is-broken' : ''}${align ? ` align-${align}` : ''}`}
      data-drag-handle
    >
      <div className="note-image-frame" contentEditable={false}>
        {!broken ? (
          <img
            key={src}
            src={src}
            alt={alt}
            title={title || alt}
            draggable={false}
            style={width ? { width: /^\d+$/.test(width) ? `${width}px` : width, maxWidth: '100%' } : undefined}
            onLoad={() => setBroken(false)}
            onError={() => {
              // Vault-relative src 404s until attachment inventory is applied.
              if (!isResolvedDisplaySrc(src)) return
              setBroken(true)
            }}
            onClick={() => {
              if (lightbox) setLightOn(true)
            }}
          />
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
      {caption && <figcaption className="jotdex-figcaption">{caption}</figcaption>}
      {selected && editor.isEditable && (
        <div className="jotdex-image-inspector" contentEditable={false}>
          {runtime && <p className="muted">Upload still running — runtime URL will not be saved.</p>}
          <label>
            Alt
            <input
              value={alt}
              onChange={(e) => updateAttributes({ alt: e.target.value })}
            />
          </label>
          <label>
            Caption
            <input
              value={caption}
              onChange={(e) => updateAttributes({ caption: e.target.value })}
            />
          </label>
          <label>
            Width
            <input
              value={width}
              placeholder="px"
              onChange={(e) => updateAttributes({ width: e.target.value || null })}
            />
          </label>
          <label>
            Align
            <select
              value={align}
              onChange={(e) => updateAttributes({ align: e.target.value || null })}
            >
              <option value="">Default</option>
              <option value="left">Left</option>
              <option value="center">Center</option>
              <option value="right">Right</option>
            </select>
          </label>
          <label>
            <input
              type="checkbox"
              checked={lightbox}
              onChange={(e) => updateAttributes({ lightbox: e.target.checked })}
            />
            Lightbox
          </label>
          <button
            type="button"
            onClick={() => {
              const replace = (editor.storage as { jotdexReplaceImage?: () => void }).jotdexReplaceImage
              replace?.()
            }}
          >
            Replace
          </button>
        </div>
      )}
      {lightOn && (
        <div
          className="jotdex-lightbox"
          role="dialog"
          onClick={() => setLightOn(false)}
          onKeyDown={(e) => {
            if (e.key === 'Escape') setLightOn(false)
          }}
        >
          <img src={src} alt={alt} />
        </div>
      )}
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
