import Highlight from '@tiptap/extension-highlight'
import Underline from '@tiptap/extension-underline'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
import type { JSONContent, MarkdownParseHelpers, MarkdownRendererHelpers, MarkdownToken } from '@tiptap/core'

function htmlMarkSpec(mark: string, tag: string, braceName: string) {
  const open = `<${tag}>`
  const close = `</${tag}>`
  const brace = `{${braceName}:`
  return {
    parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => {
      const tokens = (token as { tokens?: MarkdownToken[] }).tokens
      const text = String((token as { text?: string }).text ?? '')
      const content = tokens?.length ? helpers.parseInline(tokens) : [helpers.createTextNode(text)]
      return helpers.applyMark(mark, content)
    },
    renderMarkdown: (node: JSONContent, helpers: MarkdownRendererHelpers) =>
      `${open}${helpers.renderChildren(node.content || [])}${close}`,
    markdownTokenizer: {
      name: mark,
      level: 'inline' as const,
      start: (src: string) => {
        const a = src.toLowerCase().indexOf(open)
        const b = src.indexOf(brace)
        const hits = [a, b].filter((i) => i >= 0)
        return hits.length ? Math.min(...hits) : -1
      },
      tokenize(src: string, _t: unknown, lexer: { inlineTokens: (s: string) => MarkdownToken[] }) {
        const html = new RegExp(`^<${tag}>([\\s\\S]*?)</${tag}>`, 'i').exec(src)
        if (html) {
          return { type: mark, raw: html[0], text: html[1], tokens: lexer.inlineTokens(html[1] ?? '') }
        }
        const br = new RegExp(`^\\{${braceName}:([^}]*)\\}`).exec(src)
        if (br) {
          const inner = decodeURIComponent(br[1] ?? '')
          return { type: mark, raw: br[0], text: inner, tokens: lexer.inlineTokens(inner) }
        }
      },
    },
  }
}

export const JotdexHighlight = Highlight.extend({
  markdownTokenName: 'highlight',
  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => {
    const tokens = (token as { tokens?: MarkdownToken[] }).tokens
    const text = String((token as { text?: string }).text ?? '')
    const content = tokens?.length ? helpers.parseInline(tokens) : [helpers.createTextNode(text)]
    return helpers.applyMark('highlight', content)
  },
  renderMarkdown: (node: JSONContent, helpers: MarkdownRendererHelpers) =>
    `==${helpers.renderChildren(node.content || [])}==`,
  markdownTokenizer: {
    name: 'highlight',
    level: 'inline',
    start: (src: string) => {
      const eq = src.indexOf('==')
      const mark = src.toLowerCase().indexOf('<mark>')
      const brace = src.indexOf('{jotdex-mark:')
      const hits = [eq, mark, brace].filter((i) => i >= 0)
      return hits.length ? Math.min(...hits) : -1
    },
    tokenize(src: string, _t: unknown, lexer: { inlineTokens: (s: string) => MarkdownToken[] }) {
      const eq = /^==([^=\n]+)==/.exec(src)
      if (eq) {
        return { type: 'highlight', raw: eq[0], text: eq[1], tokens: lexer.inlineTokens(eq[1] ?? '') }
      }
      const html = /^<mark>([\s\S]*?)<\/mark>/i.exec(src)
      if (html) {
        return { type: 'highlight', raw: html[0], text: html[1], tokens: lexer.inlineTokens(html[1] ?? '') }
      }
      const br = /^\{jotdex-mark:([^}]*)\}/.exec(src)
      if (br) {
        const inner = decodeURIComponent(br[1] ?? '')
        return { type: 'highlight', raw: br[0], text: inner, tokens: lexer.inlineTokens(inner) }
      }
    },
  },
}).configure({ multicolor: false })

export const JotdexUnderline = Underline.extend(htmlMarkSpec('underline', 'u', 'jotdex-u'))
export const JotdexSubscript = Subscript.extend(htmlMarkSpec('subscript', 'sub', 'jotdex-sub'))
export const JotdexSuperscript = Superscript.extend(htmlMarkSpec('superscript', 'sup', 'jotdex-sup'))
