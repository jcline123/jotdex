import { Node, mergeAttributes } from '@tiptap/core'
import type { JSONContent, MarkdownParseHelpers, MarkdownRendererHelpers, MarkdownToken } from '@tiptap/core'
import katex from 'katex'

export const MATH_INLINE = 'mathInline'
export const MATH_BLOCK = 'mathBlock'

function renderKatex(src: string, displayMode: boolean): string {
  try {
    return katex.renderToString(src, { throwOnError: false, trust: false, displayMode, output: 'html' })
  } catch {
    return src
  }
}

export const JotdexMathInline = Node.create({
  name: MATH_INLINE,
  group: 'inline',
  inline: true,
  atom: true,

  addAttributes() {
    return { src: { default: '' } }
  },

  parseHTML() {
    return [{ tag: 'span[data-jotdex-math="inline"]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    const src = String(node.attrs.src ?? '')
    return [
      'span',
      mergeAttributes(HTMLAttributes, {
        'data-jotdex-math': 'inline',
        class: 'jotdex-math jotdex-math-inline',
        title: src,
      }),
    ]
  },

  addNodeView() {
    return ({ node }) => {
      const dom = document.createElement('span')
      dom.className = 'jotdex-math jotdex-math-inline'
      dom.setAttribute('data-jotdex-math', 'inline')
      const src = String(node.attrs.src ?? '')
      dom.title = src
      dom.innerHTML = renderKatex(src, false)
      return { dom }
    }
  },

  parseMarkdown: (token: MarkdownToken) => ({
    type: MATH_INLINE,
    attrs: { src: String((token as { src?: string }).src ?? '') },
  }),

  markdownTokenizer: {
    name: MATH_INLINE,
    level: 'inline',
    start: '\\(',
    tokenize(src: string) {
      const m = /^\\\(([\s\S]*?)\\\)/.exec(src)
      if (!m) return
      return { type: MATH_INLINE, raw: m[0], src: m[1] }
    },
  },

  renderMarkdown: (node: JSONContent) => `\\(${String(node.attrs?.src ?? '')}\\)`,
})

export const JotdexMathBlock = Node.create({
  name: MATH_BLOCK,
  group: 'block',
  atom: true,

  addAttributes() {
    return { src: { default: '' } }
  },

  parseHTML() {
    return [{ tag: 'div[data-jotdex-math="block"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-jotdex-math': 'block', class: 'jotdex-math jotdex-math-block' })]
  },

  addNodeView() {
    return ({ node }) => {
      const dom = document.createElement('div')
      dom.className = 'jotdex-math jotdex-math-block'
      dom.setAttribute('data-jotdex-math', 'block')
      const src = String(node.attrs.src ?? '')
      dom.title = src
      dom.innerHTML = renderKatex(src, true)
      return { dom }
    }
  },

  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) =>
    helpers.createNode(MATH_BLOCK, { src: String((token as { src?: string }).src ?? '') }),

  markdownTokenizer: {
    name: MATH_BLOCK,
    level: 'block',
    start: '\\[',
    tokenize(src: string) {
      const m = /^\\\[([\s\S]*?)\\\]/.exec(src)
      if (!m) return
      return { type: MATH_BLOCK, raw: m[0], src: m[1] }
    },
  },

  renderMarkdown: (node: JSONContent, _h: MarkdownRendererHelpers) => `\\[${String(node.attrs?.src ?? '')}\\]\n\n`,
})
