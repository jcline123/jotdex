import { Node, mergeAttributes } from '@tiptap/core'
import type { JSONContent, MarkdownParseHelpers, MarkdownToken } from '@tiptap/core'
import { isSafeHref } from './linkSchemes'

export const BOOKMARK_NODE = 'bookmarkCard'

export const JotdexBookmarkCard = Node.create({
  name: BOOKMARK_NODE,
  group: 'block',
  atom: true,

  addAttributes() {
    return {
      href: { default: '' },
      title: { default: '' },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-jotdex-link-card]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    const href = String(node.attrs.href ?? '')
    const title = String(node.attrs.title ?? href)
    return [
      'div',
      mergeAttributes(HTMLAttributes, { 'data-jotdex-link-card': '1', class: 'jotdex-link-card' }),
      ['a', { href, class: 'jotdex-link-card-a' }, title],
    ]
  },

  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => {
    const href = String((token as { href?: string }).href ?? '')
    const title = String((token as { title?: string }).title ?? href)
    if (!isSafeHref(href)) return []
    return helpers.createNode(BOOKMARK_NODE, { href, title })
  },

  markdownTokenizer: {
    name: BOOKMARK_NODE,
    level: 'block',
    start: (src: string) => {
      const a = src.indexOf('{jotdex-link-card}')
      const b = src.indexOf('<!-- jotdex-link-card')
      const hits = [a, b].filter((i) => i >= 0)
      return hits.length ? Math.min(...hits) : -1
    },
    tokenize(src: string) {
      const m =
        /^(?:\{jotdex-link-card\}|<!--\s*jotdex-link-card\s*-->)\s*\n?\[([^\]]*)\]\(([^)]+)\)/.exec(src)
      if (!m) return
      return { type: BOOKMARK_NODE, raw: m[0], title: m[1], href: m[2] }
    },
  },

  renderMarkdown: (node: JSONContent) => {
    const href = String(node.attrs?.href ?? '')
    const title = String(node.attrs?.title ?? href)
    return `<!-- jotdex-link-card -->\n[${title}](${href})\n\n`
  },
})
