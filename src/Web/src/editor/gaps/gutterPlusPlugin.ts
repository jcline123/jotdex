import { GapCursor } from '@tiptap/pm/gapcursor'
import { Plugin, PluginKey, TextSelection } from '@tiptap/pm/state'
import { Extension } from '@tiptap/core'
import { newOperationId, setOperationMeta } from '../operations/operationMeta'

const key = new PluginKey('jotdexGutterPlus')

export type GutterPlusState = {
  visible: boolean
  top: number
  left: number
  pos: number
  fromGap: boolean
} | null

type Options = {
  onChange?: (state: GutterPlusState) => void
}

function coordsForPos(view: { coordsAtPos: (pos: number) => { top: number; left: number } }, pos: number) {
  try {
    return view.coordsAtPos(pos)
  } catch {
    return null
  }
}

export const GutterPlusPlugin = Extension.create<Options>({
  name: 'jotdexGutterPlus',

  addOptions() {
    return { onChange: undefined }
  },

  addProseMirrorPlugins() {
    const onChange = this.options.onChange
    return [
      new Plugin({
        key,
        view: () => ({
          update: (view) => {
            if (!view.editable) {
              onChange?.(null)
              return
            }
            const { selection } = view.state
            if (selection instanceof GapCursor) {
              const c = coordsForPos(view, selection.from)
              if (!c) {
                onChange?.(null)
                return
              }
              onChange?.({ visible: true, top: c.top, left: c.left, pos: selection.from, fromGap: true })
              return
            }
            onChange?.(null)
          },
          destroy: () => onChange?.(null),
        }),
      }),
    ]
  },
})

export function insertTransientParagraphAt(view: { state: import('@tiptap/pm/state').EditorState; dispatch: (tr: import('@tiptap/pm/state').Transaction) => void }, pos: number): boolean {
  const paragraph = view.state.schema.nodes.paragraph
  const node = paragraph?.createAndFill()
  if (!node) return false
  const tr = view.state.tr.insert(pos, node)
  tr.setSelection(TextSelection.near(tr.doc.resolve(pos + 1)))
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'ui-transient',
    serializable: false,
    commitBoundary: false,
    suppressAutosave: true,
  })
  view.dispatch(tr.scrollIntoView())
  return true
}

export function deleteTransientEmptyParagraph(editor: { state: import('@tiptap/pm/state').EditorState; view: { dispatch: (tr: import('@tiptap/pm/state').Transaction) => void } }): boolean {
  const { $from } = editor.state.selection
  const parent = $from.parent
  if (parent.type.name !== 'paragraph' || parent.content.size > 0) return false
  const from = $from.before()
  const tr = editor.state.tr.delete(from, from + parent.nodeSize)
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'ui-transient',
    serializable: false,
    commitBoundary: false,
    suppressAutosave: true,
  })
  editor.view.dispatch(tr)
  return true
}

export { key as gutterPlusKey }
