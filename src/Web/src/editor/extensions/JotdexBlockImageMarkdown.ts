import Image from '@tiptap/extension-image'
import { ReactNodeViewRenderer } from '@tiptap/react'
import type { JSONContent, MarkdownParseHelpers, MarkdownToken } from '@tiptap/core'
import { ImageView } from '../../ImageView'

function imageMarkdown(node: JSONContent): string {
  const src = String(node.attrs?.src ?? '')
  const alt = String(node.attrs?.alt ?? '')
  const title = node.attrs?.title != null && String(node.attrs.title).length ? String(node.attrs.title) : ''
  const safeSrc = src.replace(/[()]/g, '\\$&')
  const titlePart = title ? ` "${String(title).replace(/"/g, '\\"')}"` : ''
  return `![${alt.replace(/]/g, '\\]')}](${safeSrc}${titlePart})`
}

const officialImageMarkdown = {
  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) =>
    helpers.createNode('image', {
      src: (token as { href?: string }).href,
      title: (token as { title?: string }).title,
      alt: (token as { text?: string }).text,
    }),
  renderMarkdown: (node: JSONContent) => imageMarkdown(node),
}

export const JotdexBlockImage = Image.extend({
  name: 'image',
  addNodeView() {
    return ReactNodeViewRenderer(ImageView)
  },
  ...officialImageMarkdown,
}).configure({
  allowBase64: false,
  inline: false,
  HTMLAttributes: { class: 'note-image-img' },
})

export const JotdexBlockImageHeadless = Image.extend({
  name: 'image',
  ...officialImageMarkdown,
}).configure({
  allowBase64: false,
  inline: false,
  HTMLAttributes: { class: 'note-image-img' },
})
