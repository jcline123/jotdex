import type { Editor } from '@tiptap/core'
import { PENDING_ASSET_NODE, type PendingAssetAttrs } from '../extensions/PendingAssetPlaceholder'
import { setOperationMeta, newOperationId } from '../operations/operationMeta'
import { dataUrlToFile } from '../../pasteHtml'

export type PasteUploadResult = {
  success: boolean
  attachmentId?: string
  markdownPath?: string
  fileName?: string
  error?: string
  note?: { etag?: string; attachments?: { id: string; fileName: string; contentType: string }[] }
}

export type PasteSessionDeps = {
  noteId: string
  noteSessionId: string
  uploadFile: (noteId: string, file: File) => Promise<PasteUploadResult>
  importRemote: (noteId: string, url: string) => Promise<PasteUploadResult>
  onAttachments?: (attachments: NonNullable<PasteUploadResult['note']>['attachments']) => void
  onStatus?: (message: string | null) => void
  onError?: (message: string) => void
  getNoteSessionId: () => string
}

export type PasteImageJob = {
  uploadId: string
  kind: 'file' | 'data' | 'remote'
  file?: File
  remoteUrl?: string
  alt?: string
}

type Session = {
  pasteSessionId: string
  noteId: string
  noteSessionId: string
  jobs: PasteImageJob[]
  aborted: boolean
}

const sessions = new Map<string, Session>()
const retryJobs = new Map<string, { job: PasteImageJob; deps: PasteSessionDeps }>()

function markPendingUploading(editor: Editor, uploadId: string): void {
  const { state } = editor
  let foundPos: number | null = null
  state.doc.descendants((node, pos) => {
    if (foundPos != null) return
    if (node.type.name === PENDING_ASSET_NODE && node.attrs.uploadId === uploadId) foundPos = pos
  })
  if (foundPos == null) return
  const node = state.doc.nodeAt(foundPos)
  if (!node) return
  const tr = state.tr.setNodeMarkup(foundPos, undefined, { ...node.attrs, status: 'uploading', error: null })
  tr.setMeta('addToHistory', false)
  editor.view.dispatch(tr)
}

export async function retryPendingUpload(
  editor: Editor,
  uploadId: string,
): Promise<{ imported: number; failed: number; lastMeta?: PasteUploadResult['note'] }> {
  const rec = retryJobs.get(uploadId)
  if (!rec) return { imported: 0, failed: 1 }
  markPendingUploading(editor, uploadId)
  return runPasteSession(editor, rec.deps, [rec.job])
}

export function hasPendingAssets(editor: Editor): boolean {
  let found = false
  editor.state.doc.descendants((node) => {
    if (node.type.name === PENDING_ASSET_NODE) found = true
  })
  return found
}

export function insertPendingAssetAtSelection(editor: Editor, attrs: PendingAssetAttrs): number | null {
  const { from } = editor.state.selection
  const node = editor.schema.nodes[PENDING_ASSET_NODE]?.create(attrs)
  if (!node) return null
  const tr = editor.state.tr.replaceSelectionWith(node, false)
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'paste-rich',
    serializable: false,
    commitBoundary: false,
    suppressAutosave: true,
    pasteSessionId: attrs.pasteSessionId,
  })
  editor.view.dispatch(tr.scrollIntoView())
  return from
}

function replacePendingByUploadId(
  editor: Editor,
  uploadId: string,
  imageAttrs: { src: string; alt?: string },
  pasteSessionId: string,
): boolean {
  const { state } = editor
  let foundPos: number | null = null
  state.doc.descendants((node, pos) => {
    if (foundPos != null) return
    if (node.type.name === PENDING_ASSET_NODE && node.attrs.uploadId === uploadId) foundPos = pos
  })
  if (foundPos == null) return false
  const imageType = state.schema.nodes.image
  if (!imageType) return false
  const image = imageType.create({ src: imageAttrs.src, alt: imageAttrs.alt ?? 'image' })
  const node = state.doc.nodeAt(foundPos)
  if (!node) return false
  const tr = state.tr.replaceWith(foundPos, foundPos + node.nodeSize, image)
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'image-resolve',
    serializable: false,
    commitBoundary: false,
    suppressAutosave: true,
    pasteSessionId,
  })
  editor.view.dispatch(tr)
  return true
}

function markPendingFailed(editor: Editor, uploadId: string, error: string): void {
  const { state } = editor
  let foundPos: number | null = null
  state.doc.descendants((node, pos) => {
    if (foundPos != null) return
    if (node.type.name === PENDING_ASSET_NODE && node.attrs.uploadId === uploadId) foundPos = pos
  })
  if (foundPos == null) return
  const node = state.doc.nodeAt(foundPos)
  if (!node) return
  const tr = state.tr.setNodeMarkup(foundPos, undefined, { ...node.attrs, status: 'failed', error })
  tr.setMeta('addToHistory', false)
  editor.view.dispatch(tr)
}

export function removePendingAsset(editor: Editor, uploadId: string): void {
  const { state } = editor
  let foundPos: number | null = null
  state.doc.descendants((node, pos) => {
    if (foundPos != null) return
    if (node.type.name === PENDING_ASSET_NODE && node.attrs.uploadId === uploadId) foundPos = pos
  })
  if (foundPos == null) return
  const node = state.doc.nodeAt(foundPos)
  if (!node) return
  editor.view.dispatch(state.tr.delete(foundPos, foundPos + node.nodeSize))
}

export async function runPasteSession(
  editor: Editor,
  deps: PasteSessionDeps,
  jobs: PasteImageJob[],
): Promise<{ imported: number; failed: number; lastMeta?: PasteUploadResult['note'] }> {
  const pasteSessionId = jobs[0]?.uploadId ? jobs[0].uploadId.split(':')[0] : crypto.randomUUID()
  const session: Session = {
    pasteSessionId,
    noteId: deps.noteId,
    noteSessionId: deps.noteSessionId,
    jobs,
    aborted: false,
  }
  sessions.set(pasteSessionId, session)
  deps.onStatus?.('Uploading…')

  let imported = 0
  let failed = 0
  let lastMeta: PasteUploadResult['note'] | undefined

  const stillValid = () =>
    !session.aborted && deps.getNoteSessionId() === session.noteSessionId && deps.noteId === session.noteId

  await Promise.all(
    jobs.map(async (job) => {
      try {
        let result: PasteUploadResult
        if (job.kind === 'remote' && job.remoteUrl) {
          result = await deps.importRemote(deps.noteId, job.remoteUrl)
        } else if (job.file) {
          result = await deps.uploadFile(deps.noteId, job.file)
        } else {
          failed++
          return
        }
        if (!stillValid()) return
        if (!result.success || !result.markdownPath) {
          failed++
          retryJobs.set(job.uploadId, { job, deps })
          markPendingFailed(editor, job.uploadId, result.error ?? 'Upload failed')
          return
        }
        retryJobs.delete(job.uploadId)
        // Resolver must know the new file before the image node mounts, or ImageView
        // loads a vault-relative src against the app origin, 404s, and sticks on broken.
        if (result.note?.attachments) deps.onAttachments?.(result.note.attachments)
        replacePendingByUploadId(
          editor,
          job.uploadId,
          { src: result.markdownPath, alt: result.fileName ?? job.alt },
          pasteSessionId,
        )
        lastMeta = result.note ?? lastMeta
        imported++
      } catch (e) {
        if (!stillValid()) return
        failed++
        retryJobs.set(job.uploadId, { job, deps })
        markPendingFailed(editor, job.uploadId, e instanceof Error ? e.message : 'Upload failed')
      }
    }),
  )

  sessions.delete(pasteSessionId)
  return { imported, failed, lastMeta }
}

export function abortPasteSession(pasteSessionId: string): void {
  const s = sessions.get(pasteSessionId)
  if (s) s.aborted = true
}

export function dataImageToFile(src: string, fileName: string): File | null {
  const m = /^data:(image\/[a-z0-9.+-]+);base64,([a-z0-9+/=\s]+)$/i.exec(src)
  if (!m) return null
  return dataUrlToFile(m[1], m[2].replace(/\s+/g, ''), fileName)
}

export function rewritePastedImagesToPlaceholders(
  html: string,
  pasteSessionId: string,
): { html: string; jobs: PasteImageJob[] } {
  const doc = new DOMParser().parseFromString(`<div id="jotdex-root">${html}</div>`, 'text/html')
  const root = doc.getElementById('jotdex-root') ?? doc.body
  const jobs: PasteImageJob[] = []
  const replaceWithPlaceholder = (img: Element, uploadId: string, alt: string) => {
    const placeholder = doc.createElement('div')
    placeholder.setAttribute('data-pending-asset', '1')
    placeholder.setAttribute('data-upload-id', uploadId)
    placeholder.setAttribute('data-paste-session', pasteSessionId)
    placeholder.setAttribute('data-alt', alt)
    placeholder.setAttribute('data-status', 'uploading')
    img.replaceWith(placeholder)
  }
  for (const img of Array.from(root.querySelectorAll('img[src]'))) {
    const src = img.getAttribute('src')?.trim() ?? ''
    const alt = img.getAttribute('alt') ?? 'image'
    const uploadId = crypto.randomUUID()
    if (src.startsWith('data:image/')) {
      const file = dataImageToFile(src, `pasted-${jobs.length + 1}.png`)
      if (file) {
        replaceWithPlaceholder(img, uploadId, alt)
        jobs.push({ uploadId, kind: 'data', file, alt })
      } else {
        img.remove()
      }
    } else if (/^https?:\/\//i.test(src) && !src.includes('paste.invalid')) {
      replaceWithPlaceholder(img, uploadId, alt)
      jobs.push({ uploadId, kind: 'remote', remoteUrl: src, alt })
    } else if (/^(file:|blob:)/i.test(src)) {
      // Browser cannot read file://; a jobless placeholder would sit forever.
      img.remove()
    }
  }
  return { html: root.innerHTML, jobs }
}
