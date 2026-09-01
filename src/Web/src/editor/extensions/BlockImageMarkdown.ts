import Image from '@tiptap/extension-image'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { ImageView } from '../../ImageView'
import { serializeBlockImage } from './serializeBlockImage'

export { serializeBlockImage }

export const BlockImage = Image.extend({
  name: 'image',
  addNodeView() {
    return ReactNodeViewRenderer(ImageView)
  },
  addStorage() {
    return {
      markdown: {
        serialize(state: Parameters<typeof serializeBlockImage>[0], node: { attrs: Record<string, unknown> }) {
          serializeBlockImage(state, node)
        },
        parse: {},
      },
    }
  },
}).configure({
  allowBase64: false,
  inline: false,
  HTMLAttributes: { class: 'note-image-img' },
})

export const BlockImageHeadless = Image.extend({
  name: 'image',
  addStorage() {
    return {
      markdown: {
        serialize(state: Parameters<typeof serializeBlockImage>[0], node: { attrs: Record<string, unknown> }) {
          serializeBlockImage(state, node)
        },
        parse: {},
      },
    }
  },
}).configure({
  allowBase64: false,
  inline: false,
  HTMLAttributes: { class: 'note-image-img' },
})
