import type { JSONContent, MarkdownParseHelpers, MarkdownRendererHelpers, MarkdownToken } from '@tiptap/core'
import { Callout, type CalloutType } from '../../callout'

const TYPES = new Set(['note', 'tip', 'warning', 'info', 'danger'])

function parseMarker(raw: string): { type: CalloutType; collapse: 'collapsed' | 'expanded' | null; title: string } | null {
  const m = /\[!(\w+)\]([+-])?(?:\s+(.*))?/i.exec(raw)
  const t = m?.[1]?.toLowerCase()
  if (!t || !TYPES.has(t)) {
    const html = /data-callout="(\w+)"/i.exec(raw)
    const h = html?.[1]?.toLowerCase()
    if (h && TYPES.has(h)) {
      return { type: h as CalloutType, collapse: null, title: '' }
    }
    return null
  }
  const collapse = m?.[2] === '-' ? 'collapsed' : m?.[2] === '+' ? 'expanded' : null
  const title = (m?.[3] ?? '').trim()
  return { type: t as CalloutType, collapse, title }
}

export const JotdexCallout = Callout.extend({
  priority: 1000,
  markdownTokenName: 'blockquote',
  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => {
    const raw = String((token as { raw?: string }).raw ?? '')
    const parsed = parseMarker(raw)
    if (!parsed) return []
    const tokens = (token as { tokens?: MarkdownToken[] }).tokens ?? []
    const content = helpers.parseChildren(tokens)
    return helpers.createNode(
      'callout',
      { type: parsed.type, title: parsed.title, collapse: parsed.collapse },
      content.length ? content : [{ type: 'paragraph' }],
    )
  },
  renderMarkdown: (node: JSONContent, helpers: MarkdownRendererHelpers) => {
    const type = String(node.attrs?.type ?? 'note')
    const title = String(node.attrs?.title ?? '').trim()
    const collapse = node.attrs?.collapse
    const mark = collapse === 'collapsed' ? '-' : collapse === 'expanded' ? '+' : ''
    const titlePart = title ? ` ${title}` : ''
    const inner = helpers.renderChildren(node.content || []).replace(/\n$/, '')
    const body = inner
      .split('\n')
      .map((line) => (line.length ? `> ${line}` : '>'))
      .join('\n')
    return `> [!${type}]${mark}${titlePart}\n${body}\n\n`
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
      const parsed = parseMarker(raw)
      if (!parsed) return
      const firstInner = taken[0]!.replace(/^>\s?/, '')
      const rest = taken.slice(1).map((l) => l.replace(/^>\s?/, ''))
      const withoutMarker = firstInner.replace(/^\[!\w+\][+-]?(?:\s+.*)?/, '').trim()
      const innerParts = [...(withoutMarker ? [withoutMarker] : []), ...rest]
      const inner = innerParts.join('\n')
      return {
        type: 'blockquote',
        raw: raw + (src[raw.length] === '\n' ? '\n' : ''),
        text: inner,
        tokens: lexer.blockTokens(inner),
        jotdexCallout: parsed.type,
        jotdexCalloutTitle: parsed.title,
        jotdexCalloutCollapse: parsed.collapse,
      }
    },
  },
})
