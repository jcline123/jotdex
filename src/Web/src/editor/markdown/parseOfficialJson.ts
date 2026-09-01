import { Editor } from '@tiptap/core'
import type { JSONContent } from '@tiptap/core'
import { createEditorExtensions } from '../extensions/createEditorExtensions'
import { rewriteCommentsToBraces } from './commentProtect'
import { closeDanglingFence } from './closeDanglingFence'
import { applyOfficialParseFixes } from './parsePostprocess'

let probe: Editor | null = null

function getProbe(): Editor {
  probe ??= new Editor({
    extensions: createEditorExtensions({ withReactNodeViews: false }),
    content: '',
  })
  return probe
}

export function parseOfficialMarkdownToJson(markdown: string): JSONContent {
  const comments = rewriteCommentsToBraces(markdown || '')
  const fences = closeDanglingFence(comments.markdown)
  const parsed = getProbe().markdown?.parse(fences.markdown) ?? { type: 'doc', content: [] }
  return applyOfficialParseFixes(parsed).doc
}
