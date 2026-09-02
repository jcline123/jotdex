import Image from '@tiptap/extension-image'
import { Extension } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import type { JSONContent, MarkdownParseHelpers, MarkdownToken } from '@tiptap/core'
import { ImageView } from '../../ImageView'

function escapeAttr(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;')
}

function imageMarkdown(node: JSONContent): string {
  const src = String(node.attrs?.src ?? '')
  if (/^(blob:|data:)/i.test(src)) return ''
  const alt = String(node.attrs?.alt ?? '')
  const title = node.attrs?.title != null && String(node.attrs.title).length ? String(node.attrs.title) : ''
  const safeSrc = src.replace(/[()]/g, '\\$&')
  const titlePart = title ? ` "${String(title).replace(/"/g, '\\"')}"` : ''
  return `![${alt.replace(/]/g, '\\]')}](${safeSrc}${titlePart})`
}

function isFigure(node: JSONContent): boolean {
  const width = node.attrs?.width
  const align = node.attrs?.align
  const caption = String(node.attrs?.caption ?? '').trim()
  const lightbox = Boolean(node.attrs?.lightbox)
  return Boolean(width) || Boolean(align) || Boolean(caption) || lightbox
}

function figureMarkdown(node: JSONContent): string {
  const src = String(node.attrs?.src ?? '')
  if (/^(blob:|data:)/i.test(src)) return ''
  const alt = String(node.attrs?.alt ?? '')
  const caption = String(node.attrs?.caption ?? '').trim()
  const width = node.attrs?.width
  const align = node.attrs?.align
  const lightbox = Boolean(node.attrs?.lightbox)
  const widthAttr = width ? ` width="${escapeAttr(String(width))}"` : ''
  const alignAttr = align ? ` data-align="${escapeAttr(String(align))}"` : ''
  const lightAttr = lightbox ? ' data-lightbox="1"' : ''
  const cap = caption ? `\n<figcaption>${escapeAttr(caption)}</figcaption>` : ''
  return `<figure class="jotdex-figure"${alignAttr}${lightAttr}>\n<img src="${escapeAttr(src)}" alt="${escapeAttr(alt)}"${widthAttr} />${cap}\n</figure>`
}

const officialImageMarkdown = {
  parseMarkdown: (token: MarkdownToken, helpers: MarkdownParseHelpers) => {
    const figure = (token as { figure?: boolean }).figure || (token as { type?: string }).type === 'jotdexFigure'
    if (figure) {
      return helpers.createNode('image', {
        src: (token as { href?: string }).href,
        alt: (token as { text?: string }).text ?? '',
        title: (token as { title?: string }).title,
        width: (token as { width?: string }).width ?? null,
        align: (token as { align?: string }).align ?? null,
        caption: (token as { caption?: string }).caption ?? '',
        lightbox: Boolean((token as { lightbox?: boolean }).lightbox),
      })
    }
    return helpers.createNode('image', {
      src: (token as { href?: string }).href,
      title: (token as { title?: string }).title,
      alt: (token as { text?: string }).text,
    })
  },
  renderMarkdown: (node: JSONContent) => (isFigure(node) ? `${figureMarkdown(node)}\n\n` : imageMarkdown(node)),
}

const figureTokenizer = {
  name: 'jotdexFigure',
  level: 'block' as const,
  start: '<figure',
  tokenize(src: string) {
    const m = /^<figure\b([^>]*)>([\s\S]*?)<\/figure>/.exec(src)
    if (!m) return
    const attrs = m[1] ?? ''
    const inner = m[2] ?? ''
    const img = /<img\b([^>]*)>/i.exec(inner)
    if (!img) return
    const imgAttrs = img[1] ?? ''
    const grab = (name: string, from: string) =>
      new RegExp(`\\b${name}="([^"]*)"`, 'i').exec(from)?.[1] ?? ''
    const srcAttr = grab('src', imgAttrs)
    const alt = grab('alt', imgAttrs)
    const width = grab('width', imgAttrs)
    const align = grab('data-align', attrs) || grab('data-align', imgAttrs)
    const lightbox = /data-lightbox="1"/.test(attrs) || /data-lightbox="1"/.test(imgAttrs)
    const cap = /<figcaption>([\s\S]*?)<\/figcaption>/i.exec(inner)?.[1]?.trim() ?? ''
    return {
      type: 'jotdexFigure',
      raw: m[0],
      href: srcAttr,
      text: alt,
      width: width || null,
      align: align || null,
      caption: cap,
      lightbox,
      figure: true,
    }
  },
}

export const JotdexBlockImage = Image.extend({
  name: 'image',
  addAttributes() {
    return {
      ...this.parent?.(),
      width: { default: null },
      align: { default: null },
      caption: { default: '' },
      lightbox: { default: false },
    }
  },
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
  addAttributes() {
    return {
      ...this.parent?.(),
      width: { default: null },
      align: { default: null },
      caption: { default: '' },
      lightbox: { default: false },
    }
  },
  ...officialImageMarkdown,
}).configure({
  allowBase64: false,
  inline: false,
  HTMLAttributes: { class: 'note-image-img' },
})

export const JotdexFigureParse = Extension.create({
  name: 'jotdexFigureParse',
  markdownTokenName: 'jotdexFigure',
  parseMarkdown: officialImageMarkdown.parseMarkdown,
  markdownTokenizer: figureTokenizer,
})
