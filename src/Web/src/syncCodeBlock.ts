import type { Editor } from '@tiptap/react'

/** Replace text inside a codeBlock at `pos` without disturbing other document content. */
export function syncCodeBlockText(editor: Editor, pos: number, text: string): boolean {
  const { state } = editor
  const node = state.doc.nodeAt(pos)
  if (!node || node.type.name !== 'codeBlock') return false

  const from = pos + 1
  const to = pos + node.nodeSize - 1
  if (from > to) return false

  const current = node.textContent
  if (current === text) return true

  const tr = state.tr.replaceWith(from, to, state.schema.text(text))
  editor.view.dispatch(tr)
  return true
}
