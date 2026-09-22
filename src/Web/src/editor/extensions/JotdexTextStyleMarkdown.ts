import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import type { JSONContent, MarkdownParseHelpers, MarkdownRendererHelpers, MarkdownToken } from '@tiptap/core'

const ALLOWED_COLORS = new Set(['#b42318', '#b54708', '#027a48', '#175cd3', '#6941c6', '#667085', '#c47b2b'])
const ALLOWED_SIZES = new Set(['0.85em', '1em', '1.25em', '1.5em'])

export function allowedColor(value: string | null | undefined): string | null {
  if (!value) return null
  const v = value.trim().toLowerCase()
  for (const c of ALLOWED_COLORS) {
    if (c.toLowerCase() === v) return c
  }
  return null
}

export function allowedFontSize(value: string | null | undefined): string | null {
  if (!value) return null
  return ALLOWED_SIZES.has(value.trim()) ? value.trim() : null
}

/** Pull allowed color / font-size from a CSS style attribute (or brace payload). */
export function parseStyleAttrs(style: string | null | undefined): {
  color: string | null
  fontSize: string | null
} {
  if (!style) return { color: null, fontSize: null }
  const colorMatch = /(?:^|;)\s*color\s*:\s*([^;]+)/i.exec(style)
  const sizeMatch = /(?:^|;)\s*font-size\s*:\s*([^;]+)/i.exec(style)
  return {
    color: allowedColor(colorMatch?.[1]?.trim()),
    fontSize: allowedFontSize(sizeMatch?.[1]?.trim()),
  }
}

export function encodeStyleBrace(color: string | null, fontSize: string | null, inner: string): string {
  const params = new URLSearchParams()
  if (color) params.set('c', color)
  if (fontSize) params.set('s', fontSize)
  params.set('t', inner)
  return `{jotdex-style:${params.toString()}}`
}

export function decodeStyleBrace(payload: string): {
  color: string | null
  fontSize: string | null
  text: string
} | null {
  try {
    const params = new URLSearchParams(payload)
    const text = params.get('t')
    if (text == null) return null
    return {
      color: allowedColor(params.get('c')),
      fontSize: allowedFontSize(params.get('s')),
      text,
    }
  } catch {
    return null
  }
}

const SPAN_RE = /^<span\s+style\s*=\s*(["'])([\s\S]*?)\1\s*>([\s\S]*?)<\/span>/i
const BRACE_RE = /^\{jotdex-style:([^}]*)\}/

function styleMarkFromToken(token: MarkdownToken, helpers: MarkdownParseHelpers) {
  const tokens = (token as { tokens?: MarkdownToken[] }).tokens
  const text = String((token as { text?: string }).text ?? '')
  const color = allowedColor((token as { color?: string }).color)
  const fontSize = allowedFontSize((token as { fontSize?: string }).fontSize)
  const content = tokens?.length ? helpers.parseInline(tokens) : [helpers.createTextNode(text)]
  if (!color && !fontSize) return content
  return helpers.applyMark('textStyle', content, {
    ...(color ? { color } : {}),
    ...(fontSize ? { fontSize } : {}),
  })
}

export const JotdexTextStyle = TextStyle.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      fontSize: {
        default: null,
        parseHTML: (element) => (element as HTMLElement).style.fontSize?.replace(/['"]+/g, '') || null,
        renderHTML: (attributes) => {
          if (!attributes.fontSize) return {}
          return { style: `font-size: ${attributes.fontSize}` }
        },
      },
    }
  },
  markdownTokenName: 'jotdexStyle',
  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => styleMarkFromToken(token, helpers),
  renderMarkdown: (node: JSONContent, helpers: MarkdownRendererHelpers) => {
    const inner = helpers.renderChildren(node.content || [])
    const color = allowedColor(node.attrs?.color as string | undefined)
    const fontSize = allowedFontSize(node.attrs?.fontSize as string | undefined)
    if (!color && !fontSize) return inner
    const parts: string[] = []
    if (color) parts.push(`color: ${color}`)
    if (fontSize) parts.push(`font-size: ${fontSize}`)
    return `<span style="${parts.join('; ')}">${inner}</span>`
  },
  markdownTokenizer: {
    name: 'jotdexStyle',
    level: 'inline' as const,
    start: (src: string) => {
      const span = src.toLowerCase().indexOf('<span')
      const brace = src.indexOf('{jotdex-style:')
      const hits = [span, brace].filter((i) => i >= 0)
      return hits.length ? Math.min(...hits) : -1
    },
    tokenize(src: string, _t: unknown, lexer: { inlineTokens: (s: string) => MarkdownToken[] }) {
      const brace = BRACE_RE.exec(src)
      if (brace) {
        const decoded = decodeStyleBrace(brace[1] ?? '')
        if (!decoded) return
        return {
          type: 'jotdexStyle',
          raw: brace[0],
          text: decoded.text,
          color: decoded.color ?? undefined,
          fontSize: decoded.fontSize ?? undefined,
          tokens: lexer.inlineTokens(decoded.text),
        }
      }
      const span = SPAN_RE.exec(src)
      if (span) {
        const { color, fontSize } = parseStyleAttrs(span[2])
        const inner = span[3] ?? ''
        return {
          type: 'jotdexStyle',
          raw: span[0],
          text: inner,
          color: color ?? undefined,
          fontSize: fontSize ?? undefined,
          tokens: lexer.inlineTokens(inner),
        }
      }
    },
  },
})

export const JotdexColor = Color.configure({ types: ['textStyle'] })
