import { Extension } from '@tiptap/core'
import type { Editor } from '@tiptap/core'
import type { JSONContent, MarkdownParseHelpers, MarkdownToken } from '@tiptap/core'
import { Node } from '@tiptap/core'
import Paragraph from '@tiptap/extension-paragraph'
import Heading from '@tiptap/extension-heading'

export type BlockAlign = 'center' | 'right' | 'justify'

export const ALIGN_MARKER = 'jotdexAlignMarker'

export const JotdexAlignMarker = Node.create({
  name: ALIGN_MARKER,
  group: 'block',
  atom: true,
  selectable: false,

  addAttributes() {
    return { align: { default: 'center' } }
  },

  parseHTML() {
    return [{ tag: 'span[data-jotdex-align-marker]' }]
  },

  renderHTML() {
    return ['span', { 'data-jotdex-align-marker': '1', class: 'sr-only' }]
  },

  parseMarkdown: (token: MarkdownToken) => ({
    type: ALIGN_MARKER,
    attrs: { align: String((token as { align?: string }).align ?? 'center') },
  }),

  markdownTokenizer: {
    name: ALIGN_MARKER,
    level: 'block',
    start: (src: string) => {
      const a = src.indexOf('{jotdex-align:')
      const b = src.indexOf('<!-- jotdex-align:')
      const hits = [a, b].filter((i) => i >= 0)
      return hits.length ? Math.min(...hits) : -1
    },
    tokenize(src: string) {
      const brace = /^\{jotdex-align:(center|right|justify)\}/.exec(src)
      if (brace) {
        return { type: ALIGN_MARKER, raw: brace[0], align: brace[1] }
      }
      const html = /^<!--\s*jotdex-align:\s*(center|right|justify)\s*-->/.exec(src)
      if (html) {
        return { type: ALIGN_MARKER, raw: html[0], align: html[1] }
      }
    },
  },

  renderMarkdown: () => '',
})

export const JotdexAlignment = Extension.create({
  name: 'jotdexAlignment',

  addGlobalAttributes() {
    return [
      {
        types: ['paragraph', 'heading'],
        attributes: {
          textAlign: {
            default: null,
            parseHTML: (element) => {
              const data = element.getAttribute('data-align')
              if (data === 'center' || data === 'right' || data === 'justify') return data
              const style = (element as HTMLElement).style?.textAlign
              if (style === 'center' || style === 'right' || style === 'justify') return style
              return null
            },
            renderHTML: (attributes) => {
              const align = attributes.textAlign as string | null
              if (!align || align === 'left') return {}
              return { 'data-align': align, style: `text-align: ${align}` }
            },
          },
        },
      },
    ]
  },
})

export function foldAlignMarkers(doc: JSONContent): { doc: JSONContent; changed: boolean } {
  const content = doc.content ?? []
  const next: JSONContent[] = []
  let changed = false
  for (let i = 0; i < content.length; i++) {
    const node = content[i]!
    if (node.type === ALIGN_MARKER) {
      const align = String(node.attrs?.align ?? '')
      const following = content[i + 1]
      if (following && (following.type === 'paragraph' || following.type === 'heading')) {
        next.push({
          ...following,
          attrs: { ...(following.attrs ?? {}), textAlign: align },
        })
        i += 1
        changed = true
        continue
      }
      changed = true
      continue
    }
    next.push(node)
  }
  return { doc: { ...doc, content: next }, changed }
}

export function renderAlignedBlock(node: JSONContent, inner: string): string {
  const align = String(node.attrs?.textAlign ?? '')
  if (align === 'center' || align === 'right' || align === 'justify') {
    return `<!-- jotdex-align: ${align} -->\n${inner}`
  }
  return inner
}

export function setBlockAlignment(editor: Editor, align: BlockAlign | null): boolean {
  const { $from } = editor.state.selection
  const types = new Set(['paragraph', 'heading'])
  for (let d = $from.depth; d > 0; d--) {
    const node = $from.node(d)
    if (types.has(node.type.name)) {
      const pos = $from.before(d)
      editor
        .chain()
        .focus()
        .command(({ tr }) => {
          tr.setNodeMarkup(pos, undefined, { ...node.attrs, textAlign: align })
          return true
        })
        .run()
      return true
    }
  }
  return false
}

export function parseAlignFromMarkdown(helpers: MarkdownParseHelpers, token: MarkdownToken) {
  return helpers.createNode(ALIGN_MARKER, { align: String((token as { align?: string }).align ?? 'center') })
}

function alignPrefix(node: JSONContent, body: string): string {
  const align = String(node.attrs?.textAlign ?? '')
  if (align === 'center' || align === 'right' || align === 'justify') {
    return `<!-- jotdex-align: ${align} -->\n${body}`
  }
  return body
}

export const JotdexParagraph = Paragraph.extend({
  renderMarkdown: (node, h, ctx) => {
    const inner = Paragraph.config.renderMarkdown?.(node, h, ctx) ?? h.renderChildren(node.content || [])
    return alignPrefix(node, inner)
  },
})

export const JotdexHeading = Heading.extend({
  renderMarkdown: (node, h, ctx) => {
    const inner = Heading.config.renderMarkdown?.(node, h, ctx) ?? `# ${h.renderChildren(node.content || [])}`
    return alignPrefix(node, inner)
  },
})

