import { Node, mergeAttributes } from '@tiptap/core'
import type { JSONContent, MarkdownToken } from '@tiptap/core'

export const RAW_HTML_COMMENT_INLINE = 'rawHtmlCommentInline'
export const RAW_HTML_COMMENT_BLOCK = 'rawHtmlCommentBlock'

function isJotdexTaskComment(raw: string): boolean {
  return /<!--\s*jotdex-(task|todo)\b/i.test(raw)
}

export const RawHtmlCommentInline = Node.create({
  name: RAW_HTML_COMMENT_INLINE,
  group: 'inline',
  inline: true,
  atom: true,
  selectable: false,

  addAttributes() {
    return { raw: { default: '' } }
  },

  parseHTML() {
    return [
      {
        tag: 'span[data-raw-html-comment]',
        getAttrs: (el) => {
          const raw = (el as HTMLElement).getAttribute('data-raw-html-comment')
          return { raw: raw ? decodeURIComponent(raw) : '' }
        },
      },
    ]
  },

  renderHTML({ HTMLAttributes }) {
    return ['span', mergeAttributes(HTMLAttributes, { 'data-raw-html-comment': '1', class: 'raw-html-comment' })]
  },

  parseMarkdown: (token: MarkdownToken) => ({
    type: RAW_HTML_COMMENT_INLINE,
    attrs: { raw: String((token as { raw?: string }).raw ?? '') },
  }),
  markdownTokenizer: {
    name: RAW_HTML_COMMENT_INLINE,
    level: 'inline',
    start: (src: string) => {
      const hits = ['<!--', '{html-comment:'].map((s) => src.indexOf(s)).filter((i) => i >= 0)
      return hits.length ? Math.min(...hits) : -1
    },
    tokenize(src: string) {
      const html = /^<!--[\s\S]*?-->/.exec(src)
      const brace = /^\{html-comment:([^}]+)\}/.exec(src)
      if (html) {
        if (isJotdexTaskComment(html[0])) return
        if (html[0].includes('\n')) return
        return { type: RAW_HTML_COMMENT_INLINE, raw: html[0], text: html[0] }
      }
      if (brace) {
        return { type: RAW_HTML_COMMENT_INLINE, raw: `<!--${decodeURIComponent(brace[1] ?? '')}-->`, text: brace[0] }
      }
    },
  },

  renderMarkdown: (node: JSONContent) => String(node.attrs?.raw ?? ''),
})

export const RawHtmlCommentBlock = Node.create({
  name: RAW_HTML_COMMENT_BLOCK,
  group: 'block',
  atom: true,

  addAttributes() {
    return { raw: { default: '' } }
  },

  parseHTML() {
    return [{ tag: 'div[data-raw-html-comment-block]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-raw-html-comment-block': '1' })]
  },

  markdownTokenizer: {
    name: RAW_HTML_COMMENT_BLOCK,
    level: 'block',
    start: '<!--',
    tokenize(src: string) {
      const m = /^<!--[\s\S]*?-->/.exec(src)
      if (!m) return
      if (isJotdexTaskComment(m[0])) return
      if (!m[0].includes('\n')) return
      return { type: RAW_HTML_COMMENT_BLOCK, raw: m[0], text: m[0] }
    },
  },

  parseMarkdown: (token: MarkdownToken) => ({
    type: RAW_HTML_COMMENT_BLOCK,
    attrs: { raw: String((token as { raw?: string }).raw ?? '') },
  }),

  renderMarkdown: (node: JSONContent) => `${String(node.attrs?.raw ?? '')}\n\n`,
})
