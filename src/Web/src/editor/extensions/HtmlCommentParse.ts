import { Extension } from '@tiptap/core'
import type { MarkdownParseHelpers, MarkdownToken } from '@tiptap/core'
import { JOTDEX_TASK_META } from './JotdexTaskMetadata'
import { RAW_HTML_COMMENT_INLINE } from './RawHtmlComment'
import { allowedColor, allowedFontSize, parseStyleAttrs } from './JotdexTextStyleMarkdown'

function parseAttrs(raw: string): Record<string, string> {
  const out: Record<string, string> = {}
  const re = /(\w+)="([^"]*)"/g
  let m: RegExpExecArray | null
  while ((m = re.exec(raw))) out[m[1]!] = m[2]!
  return out
}

const HTML_MARK: Record<string, string> = {
  u: 'underline',
  sub: 'subscript',
  sup: 'superscript',
  mark: 'highlight',
}

/** Catch HTML comment tokens that Marked emits as `html` instead of custom tokens. */
export const HtmlCommentParse = Extension.create({
  name: 'htmlCommentParse',
  markdownTokenName: 'html',
  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => {
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
    const htmlMark = /^<(u|sub|sup|mark)>([\s\S]*?)<\/\1>$/i.exec(raw)
    if (htmlMark) {
      const mark = HTML_MARK[htmlMark[1]!.toLowerCase()]
      const inner = htmlMark[2] ?? ''
      if (mark) return helpers.applyMark(mark, [helpers.createTextNode(inner)])
    }
    const styleSpan = /^<span\s+style\s*=\s*(["'])([\s\S]*?)\1\s*>([\s\S]*?)<\/span>$/i.exec(raw)
    if (styleSpan) {
      const { color, fontSize } = parseStyleAttrs(styleSpan[2])
      const inner = styleSpan[3] ?? ''
      const c = allowedColor(color)
      const s = allowedFontSize(fontSize)
      if (c || s) {
        return helpers.applyMark(
          'textStyle',
          [helpers.createTextNode(inner)],
          {
            ...(c ? { color: c } : {}),
            ...(s ? { fontSize: s } : {}),
          },
        )
      }
    }
    if (/^<!--[\s\S]*-->$/.test(raw) && !raw.includes('\n')) {
      return { type: RAW_HTML_COMMENT_INLINE, attrs: { raw } }
    }
    return []
  },
})
