import { Node, mergeAttributes } from '@tiptap/core'

export const PENDING_ASSET_NODE = 'pendingAsset'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    pendingAsset: {
      insertPendingAsset: (attrs: PendingAssetAttrs) => ReturnType
    }
  }
}

export type PendingAssetAttrs = {
  uploadId: string
  pasteSessionId: string
  alt?: string
  status?: 'uploading' | 'failed'
  error?: string
}

/** Non-persistable placeholder for in-flight image uploads. */
export const PendingAssetPlaceholder = Node.create({
  name: PENDING_ASSET_NODE,
  group: 'block',
  atom: true,
  draggable: false,
  selectable: true,

    addAttributes() {
    return {
      uploadId: { default: '', parseHTML: (el) => el.getAttribute('data-upload-id') || '' },
      pasteSessionId: { default: '', parseHTML: (el) => el.getAttribute('data-paste-session') || '' },
      alt: { default: 'Uploading image', parseHTML: (el) => el.getAttribute('data-alt') || 'Uploading image' },
      status: { default: 'uploading', parseHTML: (el) => el.getAttribute('data-status') || 'uploading' },
      error: { default: null, parseHTML: (el) => el.getAttribute('data-error') },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-pending-asset]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return [
      'div',
      mergeAttributes(HTMLAttributes, {
        'data-pending-asset': '1',
        'data-upload-id': HTMLAttributes.uploadId,
        'data-paste-session': HTMLAttributes.pasteSessionId,
        'data-alt': HTMLAttributes.alt,
        'data-status': HTMLAttributes.status,
        class: 'pending-asset',
      }),
    ]
  },

  addStorage() {
    return {
      markdown: {
        serialize() {
          /* never persist — codec/validator must block save while this node exists */
        },
        parse: {},
      },
    }
  },
})
