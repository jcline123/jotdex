import type { Editor } from '@tiptap/core'
import { getOperationMeta, type JotdexOperationMeta } from '../operations/operationMeta'
import { serializeEditorDoc, type EditorDiagnostic } from '../markdown/EditorMarkdownCodec'
import { PENDING_ASSET_NODE } from '../extensions/PendingAssetPlaceholder'
import type { Transaction } from '@tiptap/pm/state'

export type DirtyPayload = { revision: number; operationId?: string }
export type ValidatedPayload = { markdown: string; revision: number; operationId?: string }
export type ValidationErrorPayload = { revision: number; diagnostics: EditorDiagnostic[] }

export type RevisionCoordinatorOptions = {
  debounceMs?: number
  onDirty?: (p: DirtyPayload) => void
  onValidatedChange?: (p: ValidatedPayload) => void
  onValidationError?: (p: ValidationErrorPayload) => void
  onPastePending?: (pending: boolean) => void
}

function docHasPending(editor: Editor): boolean {
  let found = false
  editor.state.doc.descendants((node) => {
    if (node.type.name === PENDING_ASSET_NODE) found = true
  })
  return found
}

export class EditorRevisionCoordinator {
  editorRevision = 0
  latestSerializableRevision = 0
  latestValidatedRevision = 0
  lastSavedRevision = 0
  noteSessionId = crypto.randomUUID()
  private debounceTimer: number | null = null
  private readonly debounceMs: number
  private readonly opts: RevisionCoordinatorOptions
  private editor: Editor | null = null

  constructor(opts: RevisionCoordinatorOptions = {}) {
    this.opts = opts
    this.debounceMs = opts.debounceMs ?? 400
  }

  attach(editor: Editor): void {
    this.editor = editor
  }

  resetSession(): void {
    this.noteSessionId = crypto.randomUUID()
    this.editorRevision = 0
    this.latestSerializableRevision = 0
    this.latestValidatedRevision = 0
    this.lastSavedRevision = 0
    this.clearTimer()
  }

  markSaved(revision: number): void {
    this.lastSavedRevision = revision
  }

  observeTransaction(tr: Transaction): void {
    const meta = getOperationMeta(tr)
    if (!tr.docChanged) return
    if (meta?.kind === 'attachment-metadata') return

    this.editorRevision += 1
    this.opts.onDirty?.({ revision: this.editorRevision, operationId: meta?.operationId })

    const pending = this.editor ? docHasPending(this.editor) : false
    this.opts.onPastePending?.(pending)
    if (pending || meta?.suppressAutosave || meta?.serializable === false) return

    if (meta?.commitBoundary) {
      this.flush(meta)
      return
    }
    this.schedule(meta)
  }

  /** Serialize now (Ctrl+S, hide, paste commit). */
  flush(meta?: JotdexOperationMeta): boolean {
    this.clearTimer()
    const editor = this.editor
    if (!editor) return false
    if (docHasPending(editor)) {
      this.opts.onPastePending?.(true)
      this.opts.onValidationError?.({
        revision: this.editorRevision,
        diagnostics: [
          {
            code: 'paste-pending',
            severity: 'warning',
            message: 'Wait for uploads to finish before saving',
            operationId: meta?.operationId,
          },
        ],
      })
      return false
    }
    const result = serializeEditorDoc(editor.state.doc)
    if (!result.ok || result.markdown == null) {
      this.opts.onValidationError?.({
        revision: this.editorRevision,
        diagnostics: result.diagnostics,
      })
      return false
    }
    this.latestSerializableRevision = this.editorRevision
    this.latestValidatedRevision = this.editorRevision
    this.opts.onValidatedChange?.({
      markdown: result.markdown,
      revision: this.editorRevision,
      operationId: meta?.operationId,
    })
    return true
  }

  private schedule(meta?: JotdexOperationMeta): void {
    this.clearTimer()
    this.debounceTimer = window.setTimeout(() => {
      this.flush(meta)
    }, this.debounceMs)
  }

  private clearTimer(): void {
    if (this.debounceTimer != null) {
      window.clearTimeout(this.debounceTimer)
      this.debounceTimer = null
    }
  }

  dispose(): void {
    this.clearTimer()
  }
}
