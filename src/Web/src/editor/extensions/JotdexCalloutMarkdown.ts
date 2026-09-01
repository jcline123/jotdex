import type { JSONContent, MarkdownParseHelpers, MarkdownRendererHelpers, MarkdownToken } from '@tiptap/core'
import { Callout, type CalloutType } from '../../callout'

const TYPES = new Set(['note', 'tip', 'warning', 'info', 'danger'])

function calloutType(raw: string): CalloutType | null {
  const m = /\[!(\w+)\]/i.exec(raw)
  const t = m?.[1]?.toLowerCase()
  if (t && TYPES.has(t)) return t as CalloutType
  const html = /data-callout="(\w+)"/i.exec(raw)
  const h = html?.[1]?.toLowerCase()
  if (h && TYPES.has(h)) return h as CalloutType
  return null
}

export const JotdexCallout = Callout.extend({
  priority: 1000,
  markdownTokenName: 'blockquote',
  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => {
    const raw = String((token as { raw?: string }).raw ?? '')
    const type = calloutType(raw)
    if (!type) return []
    const tokens = (token as { tokens?: MarkdownToken[] }).tokens ?? []
    const content = helpers.parseChildren(tokens)
    return helpers.createNode('callout', { type }, content.length ? content : [{ type: 'paragraph' }])
  },
  renderMarkdown: (node: JSONContent, helpers: MarkdownRendererHelpers) => {
    const type = String(node.attrs?.type ?? 'note')
    const inner = helpers.renderChildren(node.content || []).replace(/\n$/, '')
    const body = inner
      .split('\n')
      .map((line) => (line.length ? `> ${line}` : '>'))
      .join('\n')
    return `> [!${type}]\n${body}\n\n`
  },
  markdownTokenizer: {
    name: 'blockquote',
    level: 'block',
    start: '>',
    tokenize(src: string, _tokens, lexer) {
      if (!/^>\s*\[!\w+\]/i.test(src)) return
      const lines = src.split('\n')
      const taken: string[] = []
      for (const line of lines) {
        if (line.startsWith('>') || line.trim() === '') {
          if (line.trim() === '' && taken.length && !taken[taken.length - 1]!.startsWith('>')) break
          if (line.startsWith('>')) taken.push(line)
          else if (taken.length) break
        } else break
      }
      if (!taken.length) return
      const raw = taken.join('\n')
      const type = calloutType(raw)
      if (!type) return
      const inner = taken.map((l) => l.replace(/^>\s?/, '')).join('\n').replace(/^\[!\w+\]\s*\n?/, '')
      return {
        type: 'blockquote',
        raw: raw + (src[raw.length] === '\n' ? '\n' : ''),
        text: inner,
        tokens: lexer.blockTokens(inner),
        jotdexCallout: type,
      }
    },
  },
})
