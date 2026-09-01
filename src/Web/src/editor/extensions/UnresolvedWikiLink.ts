import { Node, mergeAttributes } from '@tiptap/core'
import type { JSONContent, MarkdownToken } from '@tiptap/core'

export const UNRESOLVED_WIKI = 'unresolvedWikiLink'

export const UnresolvedWikiLink = Node.create({
  name: UNRESOLVED_WIKI,
  group: 'inline',
  inline: true,
  atom: true,

  addAttributes() {
    return { target: { default: '' } }
  },

  parseHTML() {
    return [{ tag: 'span[data-unresolved-wiki]' }]
  },

  renderHTML({ HTMLAttributes }) {
    const target = String(HTMLAttributes.target ?? '')
    return ['span', mergeAttributes(HTMLAttributes, { 'data-unresolved-wiki': target, class: 'unresolved-wiki' }), target]
  },

  markdownTokenizer: {
    name: UNRESOLVED_WIKI,
    level: 'inline',
    start: '[[',
    tokenize(src: string) {
      const m = /^\[\[([^\]\n]+)\]\]/.exec(src)
      if (!m) return
      return { type: UNRESOLVED_WIKI, raw: m[0], target: m[1] }
    },
  },

  parseMarkdown: (token: MarkdownToken) => ({
    type: UNRESOLVED_WIKI,
    attrs: { target: String((token as { target?: string }).target ?? '') },
  }),

  renderMarkdown: (node: JSONContent) => `[[${String(node.attrs?.target ?? '')}]]`,
})
