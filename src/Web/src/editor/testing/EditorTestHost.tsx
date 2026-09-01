import { useMemo, useState } from 'react'
import { NoteEditor, type AssetTransport } from '../../NoteEditor'
import type { AttachmentInfo } from '../events/noteServerEvents'

export type EditorTestHostProps = {
  markdown?: string
  noteId?: string
  attachments?: AttachmentInfo[]
  contentEpoch?: number
  uploadDelayMs?: number
  failUploads?: boolean
  reverseUploadOrder?: boolean
  onChange?: (markdown: string, revision?: number) => void
  onPastePending?: (pending: boolean) => void
}

/**
 * Mounts the production NoteEditor with fake attachment APIs and controllable upload delay.
 */
export function EditorTestHost({
  markdown = 'Hello world',
  noteId = 'test-note',
  attachments = [],
  contentEpoch = 0,
  uploadDelayMs = 15,
  failUploads = false,
  reverseUploadOrder = false,
  onChange,
  onPastePending,
}: EditorTestHostProps) {
  const [draft, setDraft] = useState(markdown)
  const seq = useMemo(() => ({ n: 0 }), [])

  const assetTransport: AssetTransport = useMemo(
    () => ({
      uploadFile: async (_id, file) => {
        seq.n += 1
        const order = seq.n
        const wait = reverseUploadOrder ? Math.max(5, uploadDelayMs * (3 - order)) : uploadDelayMs
        await new Promise((r) => setTimeout(r, wait))
        if (failUploads) return { success: false, error: 'forced failure' }
        const name = file.name || `pasted-${order}.png`
        return {
          success: true,
          attachmentId: `att-${order}`,
          markdownPath: `${noteId}.assets/${name}`,
          fileName: name,
          note: {
            etag: `etag-${order}`,
            attachments: [{ id: `att-${order}`, fileName: name, contentType: file.type || 'image/png' }],
          },
        }
      },
      importRemote: async (_id, _url) => {
        await new Promise((r) => setTimeout(r, uploadDelayMs))
        if (failUploads) return { success: false, error: 'forced failure' }
        const name = `remote-${seq.n + 1}.png`
        seq.n += 1
        return {
          success: true,
          attachmentId: `att-r-${seq.n}`,
          markdownPath: `${noteId}.assets/${name}`,
          fileName: name,
          note: {
            attachments: [{ id: `att-r-${seq.n}`, fileName: name, contentType: 'image/png' }],
          },
        }
      },
    }),
    [failUploads, noteId, reverseUploadOrder, seq, uploadDelayMs],
  )

  return (
    <NoteEditor
      noteId={noteId}
      noteStem={noteId}
      noteRelativePath={`${noteId}.md`}
      markdown={draft}
      contentEpoch={contentEpoch}
      attachments={attachments}
      assetTransport={assetTransport}
      onChange={(md, rev) => {
        setDraft(md)
        onChange?.(md, rev)
      }}
      onPastePending={onPastePending}
    />
  )
}
