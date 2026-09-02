import type { Editor } from '@tiptap/core'
import { TextSelection } from '@tiptap/pm/state'
import { newOperationId, setOperationMeta } from '../operations/operationMeta'

export function topLevelBlockRange(editor: Editor): { from: number; to: number; index: number } | null {
  const { $from } = editor.state.selection
  if ($from.depth === 0) return null
  const index = $from.index(0)
  const from = $from.before(1)
  const node = editor.state.doc.nodeAt(from)
  if (!node) return null
  return { from, to: from + node.nodeSize, index }
}

export function moveTopLevelBlock(editor: Editor, dir: -1 | 1): boolean {
  const range = topLevelBlockRange(editor)
  if (!range) return false
  const { from, to, index } = range
  const doc = editor.state.doc
  const targetIndex = index + dir
  if (targetIndex < 0 || targetIndex >= doc.childCount) return false

  const node = doc.nodeAt(from)
  if (!node) return false

  let insertPos: number
  if (dir < 0) {
    insertPos = from - doc.child(index - 1).nodeSize
  } else {
    insertPos = to + doc.child(index + 1).nodeSize - node.nodeSize
  }

  const tr = editor.state.tr.delete(from, to)
  const mappedInsert = tr.mapping.map(insertPos, dir < 0 ? -1 : 1)
  tr.insert(mappedInsert, node)
  try {
    tr.setSelection(TextSelection.near(tr.doc.resolve(mappedInsert + 1)))
  } catch {
    /* keep mapped selection */
  }
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'block-move',
    serializable: true,
    commitBoundary: true,
  })
  editor.view.dispatch(tr.scrollIntoView())
  return true
}
