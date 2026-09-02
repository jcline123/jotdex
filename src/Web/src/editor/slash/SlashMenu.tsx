import type { Editor } from '@tiptap/core'
import type { CommandContext, EditorCommandDescriptor } from '../commands/types'
import type { EditorCommandRegistry } from '../commands/createEditorCommandRegistry'
import type { SlashMenuState } from './slashMenuPlugin'

type Props = {
  editor: Editor
  state: SlashMenuState
  registry: EditorCommandRegistry
  ctx: CommandContext
  index: number
  onIndex: (i: number) => void
  onClose: () => void
}

export function SlashMenu({ editor, state, registry, ctx, index, onIndex: _onIndex, onClose }: Props) {
  if (!state?.active) return null
  const items =
    state.source === 'plus' ? registry.plusItems(state.query) : registry.slashItems(state.query)
  const enabled = items.filter((c) => c.isEnabled(editor)).slice(0, 12)
  if (!enabled.length) {
    return (
      <div className="wiki-suggest jotdex-slash" role="listbox" aria-label="Insert">
        <div className="wiki-suggest-empty">No matching commands</div>
      </div>
    )
  }
  const run = (cmd: EditorCommandDescriptor) => {
    if (state.source === 'slash') {
      editor.chain().focus().deleteRange({ from: state.from, to: state.to }).run()
    }
    registry.execute(cmd.id, ctx)
    onClose()
  }
  return (
    <div className="wiki-suggest jotdex-slash" role="listbox" aria-label="Insert">
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
