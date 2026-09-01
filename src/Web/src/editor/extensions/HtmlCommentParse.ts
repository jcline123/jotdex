import { Extension } from '@tiptap/core'
import type { MarkdownToken } from '@tiptap/core'
import { JOTDEX_TASK_META } from './JotdexTaskMetadata'
import { RAW_HTML_COMMENT_INLINE } from './RawHtmlComment'

function parseAttrs(raw: string): Record<string, string> {
  const out: Record<string, string> = {}
  const re = /(\w+)="([^"]*)"/g
  let m: RegExpExecArray | null
  while ((m = re.exec(raw))) out[m[1]!] = m[2]!
  return out
}

/** Catch HTML comment tokens that Marked emits as `html` instead of custom tokens. */
export const HtmlCommentParse = Extension.create({
  name: 'htmlCommentParse',
  markdownTokenName: 'html',
  parseMarkdown: (token: MarkdownToken) => {
    const raw = String(token.raw ?? '').trim()
    const task = /<!--\s*(jotdex-task|jotdex-todo)\s+([^>]*)-->/.exec(raw)
    if (task) {
      const attrs = parseAttrs(task[2] ?? '')
      return {
        type: JOTDEX_TASK_META,
        attrs: {
          kind: task[1] === 'jotdex-todo' ? 'todo' : 'task',
          raw,
          id: attrs.id ?? '',
          priority: attrs.priority ?? '',
          due: attrs.due ?? '',
          remind: attrs.remind ?? '',
        },
      }
    }
    if (/^<!--[\s\S]*-->$/.test(raw) && !raw.includes('\n')) {
      return { type: RAW_HTML_COMMENT_INLINE, attrs: { raw } }
    }
    return []
  },
})
