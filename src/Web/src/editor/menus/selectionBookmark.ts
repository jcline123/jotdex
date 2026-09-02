import type { Editor } from '@tiptap/core'
import type { Mapping } from '@tiptap/pm/transform'

export type SelectionBookmark = {
  noteId: string
  sessionId: string
  from: number
  to: number
}

export function captureSelectionBookmark(editor: Editor, noteId: string, sessionId: string): SelectionBookmark {
  const { from, to } = editor.state.selection
  return { noteId, sessionId, from, to }
}

export function mapSelectionBookmark(bookmark: SelectionBookmark, mapping: Mapping): SelectionBookmark {
  return {
    ...bookmark,
    from: mapping.map(bookmark.from),
    to: mapping.map(bookmark.to),
  }
}

export function restoreSelectionBookmark(
  editor: Editor,
  bookmark: SelectionBookmark,
  noteId: string,
  sessionId: string,
): { from: number; to: number } | null {
  if (bookmark.noteId !== noteId || bookmark.sessionId !== sessionId) return null
  const size = editor.state.doc.content.size
  const from = Math.max(1, Math.min(bookmark.from, size))
  const to = Math.max(from, Math.min(bookmark.to, size))
  try {
    editor.chain().focus().setTextSelection({ from, to }).run()
  } catch {
    return null
  }
  return { from, to }
}
