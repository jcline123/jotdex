import { useLayoutEffect, useState } from 'react'
import type { Editor } from '@tiptap/core'
import type { CommandContext, EditorCommandDescriptor } from '../commands/types'
import type { EditorCommandRegistry } from '../commands/createEditorCommandRegistry'
import type { SlashMenuState } from './slashMenuPlugin'

type ActiveSlashState = NonNullable<SlashMenuState>

type Props = {
  editor: Editor
  state: ActiveSlashState
  registry: EditorCommandRegistry
  ctx: CommandContext
  index: number
  onIndex: (i: number) => void
  onClose: () => void
}

type MenuPos = { top: number; left: number }

function menuPositionNearCursor(editor: Editor, docPos: number): MenuPos {
  let coords: { top: number; bottom: number; left: number }
  try {
    coords = editor.view.coordsAtPos(docPos)
  } catch {
    const sel = editor.view.dom.getBoundingClientRect()
    return { top: Math.min(sel.top + 40, window.innerHeight - 240), left: Math.max(8, sel.left + 8) }
  }

  const menuW = 260
  const menuH = 224 // matches max-height ~14rem
  const gap = 6
  let top = coords.bottom + gap
  if (top + menuH > window.innerHeight - 8) {
    top = Math.max(8, coords.top - menuH - gap)
  }
  let left = coords.left
  left = Math.min(left, window.innerWidth - menuW - 8)
  left = Math.max(8, left)
  return { top, left }
}

export function SlashMenu({ editor, state, registry, ctx, index, onIndex: _onIndex, onClose }: Props) {
  const [pos, setPos] = useState<MenuPos>(() => menuPositionNearCursor(editor, state.from))

  useLayoutEffect(() => {
    setPos(menuPositionNearCursor(editor, state.from))
  }, [editor, state.from, state.to, state.query])

  if (!state.active) return null
  const items =
    state.source === 'plus' ? registry.plusItems(state.query) : registry.slashItems(state.query)
  const enabled = items.filter((c) => c.isEnabled(editor)).slice(0, 12)
  // Never show an empty "No matching commands" plate — it was appearing on note open
  // when the caret sat after an incidental `/…` in the document.
  if (!enabled.length) return null
  const run = (cmd: EditorCommandDescriptor) => {
    if (state.source === 'slash') {
      editor.chain().focus().deleteRange({ from: state.from, to: state.to }).run()
    }
    registry.execute(cmd.id, ctx)
    onClose()
  }
  return (
    <div
      className="wiki-suggest jotdex-slash"
      role="listbox"
      aria-label="Insert"
      style={{ position: 'fixed', top: pos.top, left: pos.left, zIndex: 40 }}
    >
      {enabled.map((cmd, i) => (
        <button
          key={cmd.id}
          type="button"
          role="option"
          aria-selected={i === index}
          className={i === index ? 'on' : ''}
          onMouseDown={(e) => {
            e.preventDefault()
            run(cmd)
          }}
        >
          <span className="note-title">{cmd.label}</span>
        </button>
      ))}
    </div>
  )
}

export function slashKeydown(
  event: KeyboardEvent,
  enabled: EditorCommandDescriptor[],
  index: number,
  setIndex: (i: number) => void,
  run: (cmd: EditorCommandDescriptor) => void,
  close: () => void,
): boolean {
  if (!enabled.length) {
    if (event.key === 'Escape') {
      close()
      return true
    }
    return false
  }
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    setIndex((index + 1) % enabled.length)
    return true
  }
  if (event.key === 'ArrowUp') {
    event.preventDefault()
    setIndex((index - 1 + enabled.length) % enabled.length)
    return true
  }
  if (event.key === 'Enter') {
    event.preventDefault()
    const pick = enabled[index] ?? enabled[0]
    if (pick) run(pick)
    return true
  }
  if (event.key === 'Escape') {
    event.preventDefault()
    close()
    return true
  }
  return false
}
