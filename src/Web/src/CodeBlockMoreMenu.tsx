import { useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

type CodeBlockMoreMenuProps = {
  onInsertSnippet: () => void
  onSaveSnippet: () => void
  /** Extra items for future overflow actions. */
  children?: ReactNode
}

type MenuPos = { top: number; left: number; openUp: boolean }

/** Compact ☰ overflow for uncommon code-box actions; Copy/Edit stay on the main bar. */
export function CodeBlockMoreMenu({ onInsertSnippet, onSaveSnippet, children }: CodeBlockMoreMenuProps) {
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState<MenuPos | null>(null)
  const rootRef = useRef<HTMLDivElement>(null)
  const btnRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const menuId = useId()

  const placeMenu = () => {
    const btn = btnRef.current
    if (!btn) return
    const r = btn.getBoundingClientRect()
    const menuH = menuRef.current?.offsetHeight ?? 88
    const menuW = menuRef.current?.offsetWidth ?? 168
    const gap = 4
    const spaceBelow = window.innerHeight - r.bottom - gap
    const openUp = spaceBelow < menuH && r.top > spaceBelow
    const top = openUp ? r.top - gap - menuH : r.bottom + gap
    let left = r.right - menuW
    left = Math.max(8, Math.min(left, window.innerWidth - menuW - 8))
    setPos({ top, left, openUp })
  }

  useLayoutEffect(() => {
    if (!open) {
      setPos(null)
      return
    }
    placeMenu()
    // Second pass after the portaled menu has real metrics.
    const id = requestAnimationFrame(() => placeMenu())
    return () => cancelAnimationFrame(id)
  }, [open])

  useEffect(() => {
    if (!open) return
    const onDoc = (e: PointerEvent) => {
      const t = e.target
      if (!(t instanceof Node)) return
      if (rootRef.current?.contains(t)) return
      if (menuRef.current?.contains(t)) return
      setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    const onReposition = () => placeMenu()
    document.addEventListener('pointerdown', onDoc, true)
    document.addEventListener('keydown', onKey)
    window.addEventListener('resize', onReposition)
    window.addEventListener('scroll', onReposition, true)
    return () => {
      document.removeEventListener('pointerdown', onDoc, true)
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('resize', onReposition)
      window.removeEventListener('scroll', onReposition, true)
    }
  }, [open])

  const menu =
    open &&
    createPortal(
      <div
        className={`code-block-more-menu${pos?.openUp ? ' open-up' : ''}`}
        id={menuId}
        role="menu"
        ref={menuRef}
        style={
          pos
            ? { top: pos.top, left: pos.left }
            : { top: -9999, left: -9999, visibility: 'hidden' as const }
        }
      >
        <button
          type="button"
          role="menuitem"
          className="code-block-more-item"
          onClick={() => {
            setOpen(false)
            onInsertSnippet()
          }}
        >
          Insert snippet
        </button>
        <button
          type="button"
          role="menuitem"
          className="code-block-more-item"
          onClick={() => {
            setOpen(false)
            onSaveSnippet()
          }}
        >
          Save as snippet
        </button>
        {children}
      </div>,
      document.body,
    )

  return (
    <div className={`code-block-more${open ? ' is-open' : ''}`} ref={rootRef}>
      <button
        type="button"
        ref={btnRef}
        className="code-chrome-btn code-block-more-btn"
        aria-label="More code box actions"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        title="More"
        onClick={() => setOpen((v) => !v)}
      >
        <span className="code-block-more-icon" aria-hidden>
          <span />
          <span />
          <span />
        </span>
      </button>
      {menu}
    </div>
  )
}
