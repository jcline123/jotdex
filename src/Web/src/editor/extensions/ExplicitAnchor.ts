import { Node, mergeAttributes } from '@tiptap/core'
import type { JSONContent, MarkdownToken } from '@tiptap/core'

/** Empty HTML anchors `<a id="…"></a>` for same-note fragment targets. */
export const ExplicitAnchor = Node.create({
  name: 'explicitAnchor',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  priority: 1000,

  addAttributes() {
    return {
      id: {
        default: null,
        parseHTML: (el) => el.getAttribute('id'),
        renderHTML: (attrs) => (attrs.id ? { id: String(attrs.id) } : {}),
      },
    }
  },

  parseHTML() {
    return [
      {
        tag: 'a[id]',
        getAttrs: (el) => {
          const a = el as HTMLAnchorElement
          const id = a.getAttribute('id')
          if (!id) return false
          const href = a.getAttribute('href')
          if (href && href.length > 0 && href !== '#') return false
          if (a.textContent && a.textContent.trim().length > 0) return false
          return { id }
        },
      },
    ]
  },

  renderHTML({ HTMLAttributes }) {
    return ['a', mergeAttributes(HTMLAttributes, { class: 'jotdex-anchor' })]
  },

  parseMarkdown: (token: MarkdownToken) => ({
    type: 'explicitAnchor',
    attrs: { id: String((token as { id?: string }).id ?? '') },
  }),

  markdownTokenizer: {
    name: 'explicitAnchor',
    level: 'inline',
    start: (src: string) => {
      const brace = src.indexOf('{jotdex-anchor:')
      const html = src.search(/<a\s+[^>]*\bid\s*=/i)
      const hits = [brace, html].filter((i) => i >= 0)
      return hits.length ? Math.min(...hits) : -1
    },
    tokenize(src: string) {
      const brace = /^\{jotdex-anchor:([^}]+)\}/.exec(src)
      if (brace) {
        try {
          return {
            type: 'explicitAnchor',
            raw: brace[0],
            id: decodeURIComponent(brace[1] ?? ''),
          }
        } catch {
          return {
            type: 'explicitAnchor',
            raw: brace[0],
            id: brace[1] ?? '',
          }
        }
      }
      // Fallback when protect pass did not run (e.g. paste of already-braced form only).
      const m = /^<a\s+([^>]*?)\bid\s*=\s*(["'])([^"']+)\2([^>]*)>\s*<\/a>/i.exec(src)
      if (!m) return
      const before = m[1] ?? ''
      const after = m[4] ?? ''
      if (/\bhref\s*=/i.test(before + after)) {
        const href = /\bhref\s*=\s*(["'])([^"']*)\1/i.exec(before + after)
        if (href && href[2] && href[2] !== '#') return
      }
      return {
        type: 'explicitAnchor',
        raw: m[0],
        id: m[3],
      }
    },
  },

  renderMarkdown: (node: JSONContent) => {
    const id = String(node.attrs?.id ?? '').trim()
    if (!id) return ''
    return `<a id="${id}"></a>`
  },
})
