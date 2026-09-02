import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import { topLevelBlockRange } from './moveBlock'

const key = new PluginKey('jotdexDragHandle')

type Options = {
  onChange?: (state: { top: number; left: number; pos: number } | null) => void
}

export const DragHandlePlugin = Extension.create<Options>({
  name: 'jotdexDragHandle',

  addOptions() {
    return { onChange: undefined }
  },

  addProseMirrorPlugins() {
    const onChange = this.options.onChange
    return [
      new Plugin({
        key,
        props: {
          decorations: (state) => {
            const range = (() => {
              const { $from } = state.selection
              if ($from.depth === 0) return null
              const from = $from.before(1)
              const node = state.doc.nodeAt(from)
              if (!node) return null
              return { from, to: from + node.nodeSize }
            })()
            if (!range) return DecorationSet.empty
            return DecorationSet.create(state.doc, [
              Decoration.node(range.from, range.to, { class: 'jotdex-block-target' }),
            ])
          },
        },
        view: () => ({
          update: (view) => {
            if (!view.editable) {
              onChange?.(null)
              return
            }
            const editorLike = { state: view.state } as { state: typeof view.state }
            const range = topLevelBlockRange(editorLike as never)
            if (!range) {
              onChange?.(null)
              return
            }
            try {
              const c = view.coordsAtPos(range.from)
              onChange?.({ top: c.top, left: c.left, pos: range.from })
            } catch {
              onChange?.(null)
            }
          },
          destroy: () => onChange?.(null),
        }),
      }),
    ]
  },
})
