import { BubbleMenu } from '@tiptap/react/menus'
import type { Editor } from '@tiptap/core'
import type { EditorCommandRegistry } from '../commands/createEditorCommandRegistry'
import type { CommandContext } from '../commands/types'
import type { OverlayCoordinator } from '../menus/overlayCoordinator'

type Props = {
  editor: Editor
  registry: EditorCommandRegistry
  ctx: CommandContext
  overlays: OverlayCoordinator
  moreOpen: boolean
  onMore: (open: boolean) => void
}

const BUBBLE: Array<Parameters<EditorCommandRegistry['execute']>[0]> = [
  'mark.bold',
  'mark.italic',
  'mark.underline',
  'mark.strike',
  'mark.code',
  'mark.highlight',
  'insert.link',
]

const MORE: Array<Parameters<EditorCommandRegistry['execute']>[0]> = [
  'mark.subscript',
  'mark.superscript',
  'align.left',
  'align.center',
  'align.right',
  'align.justify',
  'format.clear',
]

export function FormattingBubbleMenu({ editor, registry, ctx, overlays, moreOpen, onMore }: Props) {
  return (
    <BubbleMenu
      editor={editor}
      className="jotdex-bubble"
      options={{ placement: 'top' }}
      shouldShow={({ editor: ed, from, to }) => {
        if (overlays.blocksBubble) return false
        if (!ed.isEditable) return false
        if (ed.isActive('codeBlock') || ed.isActive('image')) return false
        return from !== to
      }}
    >
      {BUBBLE.map((id) => {
        const cmd = registry.get(id)
        if (!cmd) return null
        return (
          <button
            key={id}
            type="button"
            className={cmd.isActive?.(editor) ? 'on' : ''}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => registry.execute(id, ctx)}
          >
            {cmd.label}
          </button>
        )
      })}
      <button
        type="button"
        className={moreOpen ? 'on' : ''}
        onMouseDown={(e) => e.preventDefault()}
        onClick={() => onMore(!moreOpen)}
      >
        More
      </button>
      {moreOpen &&
        MORE.map((id) => {
          const cmd = registry.get(id)
          if (!cmd) return null
          return (
            <button
              key={id}
              type="button"
              className={cmd.isActive?.(editor) ? 'on' : ''}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => {
                registry.execute(id, ctx)
                onMore(false)
              }}
            >
              {cmd.label}
            </button>
          )
        })}
    </BubbleMenu>
  )
}
