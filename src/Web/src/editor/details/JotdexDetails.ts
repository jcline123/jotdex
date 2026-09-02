import { Node, mergeAttributes } from '@tiptap/core'
import type { JSONContent, MarkdownParseHelpers, MarkdownRendererHelpers, MarkdownToken } from '@tiptap/core'

export const DETAILS_NODE = 'details'

export const JotdexDetails = Node.create({
  name: DETAILS_NODE,
  group: 'block',
  content: 'block+',
  defining: true,
  isolating: true,

  parseHTML() {
    return [{ tag: 'details.jotdex-details' }, { tag: 'details' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['details', mergeAttributes(HTMLAttributes, { class: 'jotdex-details' }), 0]
  },

  addCommands() {
    return {
      setDetails:
        () =>
        ({ commands }) =>
          commands.insertContent({
            type: this.name,
            content: [
              { type: 'paragraph', content: [{ type: 'text', text: 'Summary' }] },
              { type: 'paragraph', content: [{ type: 'text', text: 'Details' }] },
            ],
          }),
    }
  },

  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => {
    const tokens = (token as { tokens?: MarkdownToken[] }).tokens ?? []
    const content = helpers.parseChildren(tokens)
    return helpers.createNode(DETAILS_NODE, {}, content.length ? content : [{ type: 'paragraph' }])
  },

  markdownTokenizer: {
    name: DETAILS_NODE,
    level: 'block',
    start: (src: string) => {
      const a = src.indexOf('{jotdex-details}')
      const b = src.indexOf('<!-- jotdex-details')
      const hits = [a, b].filter((i) => i >= 0)
      return hits.length ? Math.min(...hits) : -1
    },
    tokenize(src: string, _tokens: unknown, lexer: { blockTokens: (s: string) => MarkdownToken[] }) {
      const open = /^(?:\{jotdex-details\}|<!--\s*jotdex-details\s*-->)/.exec(src)
      if (!open) return
      const rest = src.slice(open[0].length)
      const close = /\{ \/jotdex-details\}|\{\/jotdex-details\}|<!--\s*\/jotdex-details\s*-->/.exec(rest)
      if (!close) return
      const inner = rest.slice(0, close.index).replace(/^\n/, '').replace(/\n$/, '')
      const raw = open[0] + rest.slice(0, close.index + close[0].length)
      return {
        type: DETAILS_NODE,
        raw: raw + (src[raw.length] === '\n' ? '\n' : ''),
        text: inner,
        tokens: lexer.blockTokens(inner),
      }
    },
  },

  renderMarkdown: (node: JSONContent, helpers: MarkdownRendererHelpers) => {
    const inner = helpers.renderChildren(node.content || []).replace(/\n$/, '')
    return `<!-- jotdex-details -->\n${inner}\n<!-- /jotdex-details -->\n\n`
  },
})

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    details: {
      setDetails: () => ReturnType
    }
  }
}
