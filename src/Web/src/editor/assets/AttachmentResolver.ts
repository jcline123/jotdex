import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import type { Editor } from '@tiptap/core'

export type AttachmentInfo = { id: string; fileName: string; contentType: string }

export type AttachmentResolverState = {
  byFileName: Map<string, string>
  byId: Map<string, AttachmentInfo>
}

export const attachmentResolverKey = new PluginKey<AttachmentResolverState>('attachmentResolver')

export function buildResolverState(attachments: AttachmentInfo[]): AttachmentResolverState {
  const byFileName = new Map<string, string>()
  const byId = new Map<string, AttachmentInfo>()
  for (const att of attachments) {
    byId.set(att.id, att)
    const api = `/api/attachments/${att.id}`
    byFileName.set(att.fileName, api)
    try {
      byFileName.set(decodeURIComponent(att.fileName), api)
    } catch {
      /* ignore */
    }
  }
  return { byFileName, byId }
}

export function displayUrlForSrc(state: AttachmentResolverState | undefined, src: string): string {
  if (!src) return src
  if (src.startsWith('/api/attachments/') || src.startsWith('blob:') || src.startsWith('data:')) return src
  if (/^https?:\/\//i.test(src)) return src
  const fileName = src.split('/').pop()?.split('?')[0] ?? ''
  let decoded = fileName
  try {
    decoded = decodeURIComponent(fileName.replace(/\+/g, ' '))
  } catch {
    /* keep */
  }
  return state?.byFileName.get(decoded) ?? state?.byFileName.get(fileName) ?? src
}

export function getAttachmentResolverState(editor: Editor): AttachmentResolverState | undefined {
  return attachmentResolverKey.getState(editor.state)
}

export const AttachmentResolver = Extension.create<{ attachments: AttachmentInfo[] }>({
  name: 'attachmentResolver',
  addOptions() {
    return { attachments: [] as AttachmentInfo[] }
  },
  addProseMirrorPlugins() {
    const initial = buildResolverState(this.options.attachments)
    return [
      new Plugin<AttachmentResolverState>({
        key: attachmentResolverKey,
        state: {
          init: () => initial,
          apply(tr, value) {
            const next = tr.getMeta(attachmentResolverKey) as AttachmentInfo[] | undefined
            if (next) return buildResolverState(next)
            return value
          },
        },
      }),
    ]
  },
})

export function dispatchAttachmentInventory(editor: Editor, attachments: AttachmentInfo[]): void {
  const tr = editor.state.tr.setMeta(attachmentResolverKey, attachments)
  tr.setMeta('addToHistory', false)
  editor.view.dispatch(tr)
}
