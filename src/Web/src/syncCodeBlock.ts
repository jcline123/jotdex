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

/** Insert `insert` at a character offset inside the code block (does not replace existing text). */
export function insertCodeBlockText(
  editor: Editor,
  pos: number,
  offset: number,
  insert: string,
): boolean {
  const { state } = editor
  const node = state.doc.nodeAt(pos)
  if (!node || node.type.name !== 'codeBlock') return false

  const current = node.textContent
  const clamped = Math.max(0, Math.min(offset, current.length))
  const next = current.slice(0, clamped) + insert + current.slice(clamped)
  return syncCodeBlockText(editor, pos, next)
}

/** Character offset for the primary selection when it sits inside this code block; otherwise end of block. */
export function codeBlockInsertOffset(editor: Editor, blockPos: number, fallbackLength: number): number {
  const node = editor.state.doc.nodeAt(blockPos)
  if (!node || node.type.name !== 'codeBlock') return fallbackLength

  const blockStart = blockPos + 1
  const blockEnd = blockPos + node.nodeSize - 1
  const { from } = editor.state.selection
  if (from >= blockStart && from <= blockEnd) return from - blockStart
  return node.textContent.length
}
