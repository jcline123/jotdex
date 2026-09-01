import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import type { JSONContent, MarkdownRendererHelpers } from '@tiptap/core'

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
})

export const JotdexColor = Color.configure({ types: ['textStyle'] })
