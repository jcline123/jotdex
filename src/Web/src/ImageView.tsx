import { NodeViewWrapper } from '@tiptap/react'
import type { NodeViewProps } from '@tiptap/react'
import { useEffect, useRef, useState } from 'react'
import { displayUrlForSrc, getAttachmentResolverState } from './editor/assets/AttachmentResolver'
import {
  currentImageWidthPercent,
  nudgeImageWidthPercent,
  storedWidthFromPercent,
} from './editor/images/imageWidth'

function isResolvedDisplaySrc(src: string): boolean {
  return /^(?:\/api\/attachments\/|blob:|data:|https?:)/i.test(src)
}

type ResizeEdge = 'e' | 'w'

/** TipTap image with select chrome, inspector, drag-resize, and one-click Remove. */
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
  const [previewPct, setPreviewPct] = useState<number | null>(null)
  const dragRef = useRef<{
    edge: ResizeEdge
    startX: number
    startPct: number
    originPct: number
    lastPct: number
  } | null>(null)
  const frameRef = useRef<HTMLDivElement>(null)
  const resizeAbortRef = useRef<((commit: boolean) => void) | null>(null)
  const label = alt || title || fileNameFromSrc(canonical) || 'Image'
  const runtime = /^(blob:|data:)/i.test(canonical)

  useEffect(() => {
    setBroken(false)
  }, [src])

  useEffect(() => {
    if (!selected) {
      resizeAbortRef.current?.(false)
      setPreviewPct(null)
    }
  }, [selected])

  useEffect(() => {
    return () => {
      resizeAbortRef.current?.(false)
      document.body.classList.remove('jotdex-image-resizing')
    }
  }, [])

  useEffect(() => {
    if (!selected || !editor.isEditable) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && resizeAbortRef.current) {
        e.preventDefault()
        resizeAbortRef.current(false)
        return
      }
      if (resizeAbortRef.current) return
      const t = e.target as HTMLElement | null
      if (t && (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.tagName === 'TEXTAREA')) return
      if (!e.altKey || (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight')) return
      e.preventDefault()
      const container = editor.view.dom.clientWidth
      const displayed = frameRef.current?.getBoundingClientRect().width ?? container
      const current = currentImageWidthPercent(width || null, displayed, container)
      const next = nudgeImageWidthPercent(current, e.key === 'ArrowRight' ? 1 : -1)
      updateAttributes({ width: storedWidthFromPercent(next) })
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selected, editor, width, updateAttributes])

  function commitPercent(pct: number) {
    updateAttributes({ width: storedWidthFromPercent(pct) })
  }

  function startResize(edge: ResizeEdge, clientX: number) {
    if (!editor.isEditable) return
    resizeAbortRef.current?.(false)
    const container = editor.view.dom.clientWidth
    const displayed = frameRef.current?.getBoundingClientRect().width ?? container
    const startPct = currentImageWidthPercent(width || null, displayed, container)
    dragRef.current = { edge, startX: clientX, startPct, originPct: startPct, lastPct: startPct }
    setPreviewPct(startPct)
    document.body.classList.add('jotdex-image-resizing')

    const onMove = (ev: PointerEvent) => {
      const drag = dragRef.current
      if (!drag) return
      const delta = drag.edge === 'e' ? ev.clientX - drag.startX : drag.startX - ev.clientX
      const containerW = editor.view.dom.clientWidth || 1
      const next = currentImageWidthPercent(null, (drag.startPct / 100) * containerW + delta, containerW)
      drag.lastPct = next
      setPreviewPct(next)
    }
    const finish = (commit: boolean) => {
      if (resizeAbortRef.current === finish) resizeAbortRef.current = null
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', onUp)
      window.removeEventListener('pointercancel', onCancel)
      document.body.classList.remove('jotdex-image-resizing')
      const drag = dragRef.current
      dragRef.current = null
      setPreviewPct(null)
      if (commit && drag && drag.lastPct !== drag.originPct) commitPercent(drag.lastPct)
    }
    const onUp = () => finish(true)
    const onCancel = () => finish(false)
    resizeAbortRef.current = finish
    window.addEventListener('pointermove', onMove)
    window.addEventListener('pointerup', onUp)
    window.addEventListener('pointercancel', onCancel)
  }

  const wrapperWidth =
    previewPct != null
      ? `${previewPct}%`
      : width.endsWith('%')
        ? width
        : width && /^\d+(\.\d+)?$/.test(width)
          ? `${width}px`
          : width || undefined

  const hasWidth = wrapperWidth != null

  return (
    <NodeViewWrapper
      className={`note-image${selected ? ' is-selected' : ''}${broken ? ' is-broken' : ''}${align ? ` align-${align}` : ''}${previewPct != null ? ' is-resizing' : ''}${hasWidth ? ' has-width' : ''}`}
      data-drag-handle={previewPct != null ? undefined : ''}
      style={wrapperWidth ? { width: wrapperWidth, maxWidth: '100%' } : undefined}
    >
      <div className="note-image-frame" ref={frameRef} contentEditable={false}>
        {!broken ? (
          <img
            key={src}
            src={src}
            alt={alt}
            title={title || alt}
            draggable={false}
            onLoad={() => setBroken(false)}
            onError={() => {
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
        {selected && editor.isEditable && !broken && (
          <>
            {(['nw', 'ne', 'sw', 'se', 'w', 'e'] as const).map((name) => (
              <span
                key={name}
                className={`note-image-resize note-image-resize-${name}`}
                data-resize={name}
                onPointerDown={(e) => {
                  e.preventDefault()
                  e.stopPropagation()
                  startResize(name === 'w' || name === 'nw' || name === 'sw' ? 'w' : 'e', e.clientX)
                }}
                onDragStart={(e) => e.preventDefault()}
              />
            ))}
          </>
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
              value={previewPct != null ? `${previewPct}%` : width}
              placeholder="65% or px"
              disabled={previewPct != null}
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
          <p className="muted jotdex-image-resize-hint">Drag a corner or edge to resize. Alt+←/→ nudges 5%. Escape cancels a drag.</p>
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
