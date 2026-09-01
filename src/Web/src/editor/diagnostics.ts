/** Development-only editor diagnostics. Never log note or clipboard content. */

export type EditorDiagEvent = {
  ts?: number
  noteId?: string
  noteSessionId?: string
  editorRevision?: number
  saveRevision?: number
  operationId?: string
  kind?: string
  pasteSessionId?: string
  setContentReason?: string
  nodeCounts?: Record<string, number>
  serializeMs?: number
  validatorCodes?: string[]
}

const enabled = () => {
  try {
    return localStorage.getItem('jotdex.editorDiagnostics') === '1'
  } catch {
    return false
  }
}

export function logEditorDiag(event: EditorDiagEvent): void {
  if (!import.meta.env.DEV && !enabled()) return
  if (!enabled() && !import.meta.env.DEV) return
  if (import.meta.env.DEV || enabled()) {
    console.info('[jotdex-editor]', { ...event, ts: event.ts || Date.now() })
  }
}
