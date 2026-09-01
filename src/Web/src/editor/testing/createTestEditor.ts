import { Editor } from '@tiptap/core'
import { createEditorExtensions } from '../extensions/createEditorExtensions'
import { serializeEditorDoc } from '../markdown/EditorMarkdownCodec'
import { parseOfficialMarkdownToJson } from '../markdown/parseOfficialJson'

export function createTestEditor(
  markdown = '',
  options: { element?: HTMLElement } = {},
): Editor {
  const element = options.element ?? document.createElement('div')
  if (!element.isConnected) document.body.appendChild(element)
  const editor = new Editor({
    element,
    extensions: createEditorExtensions({ withReactNodeViews: false }),
    content: markdown ? parseOfficialMarkdownToJson(markdown) : '',
  })
  const originalDestroy = editor.destroy.bind(editor)
  editor.destroy = () => {
    originalDestroy()
    if (!options.element) element.remove()
  }
  return editor
}

export function editorMarkdown(editor: Editor): string {
  const result = serializeEditorDoc(editor.state.doc)
  if (!result.ok) {
    throw new Error(result.diagnostics.map((d) => d.code).join(', ') || 'serialize failed')
  }
  return result.markdown ?? ''
}

export function dumpEditor(editor: Editor): { json: unknown; markdown: string } {
  const md = editor.getMarkdown?.() ?? ''
  return { json: editor.getJSON(), markdown: md }
}

export function findTextRange(editor: Editor, snippet: string): { from: number; to: number } {
  let from = -1
  editor.state.doc.descendants((node, pos) => {
    if (from >= 0 || !node.isText || !node.text) return
    const i = node.text.indexOf(snippet)
    if (i >= 0) from = pos + i
  })
  if (from < 0) throw new Error(`text not found: ${JSON.stringify(snippet)}`)
  return { from, to: from + snippet.length }
}

export function reopenMarkdown(markdown: string): { editor: Editor; markdown: string } {
  const editor = createTestEditor(markdown)
  return { editor, markdown: editorMarkdown(editor) }
}
