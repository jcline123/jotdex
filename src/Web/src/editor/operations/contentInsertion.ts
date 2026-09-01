import type { Editor, JSONContent } from '@tiptap/core'
import { applyOfficialParseFixes } from '../markdown/parsePostprocess'
import { rewriteCommentsToBraces } from '../markdown/commentProtect'
import { closeDanglingFence } from '../markdown/closeDanglingFence'

export type InsertOptions = { emitUpdate?: boolean }

export function setMarkdownDocument(editor: Editor, markdown: string, options: InsertOptions = {}) {
  const comments = rewriteCommentsToBraces(markdown || '')
  const fences = closeDanglingFence(comments.markdown)
  const parsed = editor.markdown?.parse(fences.markdown) ?? { type: 'doc', content: [] }
  const fixed = applyOfficialParseFixes(parsed)
  return editor.commands.setContent(fixed.doc, { emitUpdate: options.emitUpdate ?? false })
}

export function insertMarkdown(editor: Editor, markdown: string) {
  return editor.chain().focus().insertContent(markdown, { contentType: 'markdown' }).run()
}

export function insertHtml(editor: Editor, html: string) {
  return editor.chain().focus().insertContent(html, { contentType: 'html' }).run()
}

export function insertLiteralText(editor: Editor, text: string) {
  const { state, view } = editor
  const { from, to } = state.selection
  view.dispatch(state.tr.insertText(text, from, to))
  return true
}

export function replaceWithJson(editor: Editor, json: JSONContent, options: InsertOptions = {}) {
  return editor.commands.setContent(json, { emitUpdate: options.emitUpdate ?? false })
}
