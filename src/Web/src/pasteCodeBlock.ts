import type { Editor } from '@tiptap/react'
import { plainTextFromClipboard } from './copyCodePlain'

/** True when the selection sits inside a fenced code block node. */
export function isSelectionInCodeBlock(editor: Editor): boolean {
  return editor.isActive('codeBlock')
}

/** Plain text for paste into a code box — never interpret clipboard HTML as document structure. */
export function plainTextForCodeBoxPaste(rawPlain: string, html: string): string {
  const plain = plainTextFromClipboard(rawPlain, html).replace(/\r\n/g, '\n').replace(/\r/g, '\n')
  if (plain.trim()) return plain
  if (!html?.trim()) return plain
  const doc = new DOMParser().parseFromString(html, 'text/html')
  return (doc.body.textContent ?? '').replace(/\u00a0/g, ' ').replace(/\r\n/g, '\n').replace(/\r/g, '\n')
}

/**
 * Insert plain characters into the active code block without creating sibling blocks.
 * ProseMirror `insertContent` and rich HTML paste can split multiline paste outside the box.
 */
export function pastePlainIntoCodeBlock(editor: Editor, text: string): boolean {
  if (!isSelectionInCodeBlock(editor)) return false

  const normalized = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
  const { from, to } = editor.state.selection
  editor.view.dispatch(editor.state.tr.insertText(normalized, from, to).scrollIntoView())
  return true
}
