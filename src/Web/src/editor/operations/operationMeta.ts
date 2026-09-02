import type { Transaction } from '@tiptap/pm/state'

export type JotdexOperationKind =
  | 'typing'
  | 'heading'
  | 'paste-rich'
  | 'paste-plain'
  | 'paste-code'
  | 'image-resolve'
  | 'attachment-metadata'
  | 'external-load'
  | 'history-restore'
  | 'source-convert'
  | 'ui-transient'
  | 'disclosure'
  | 'block-move'

export type JotdexOperationMeta = {
  operationId: string
  kind: JotdexOperationKind
  serializable: boolean
  commitBoundary: boolean
  suppressAutosave?: boolean
  pasteSessionId?: string
}

export const JOTDEX_OP_META = 'jotdexOperation'

export function newOperationId(): string {
  return crypto.randomUUID()
}

export function setOperationMeta(tr: Transaction, meta: JotdexOperationMeta): Transaction {
  return tr.setMeta(JOTDEX_OP_META, meta)
}

export function getOperationMeta(tr: Transaction): JotdexOperationMeta | undefined {
  return tr.getMeta(JOTDEX_OP_META) as JotdexOperationMeta | undefined
}

export const ALLOWED_SET_CONTENT_REASONS = [
  'initial-load',
  'user-reload',
  'history-restore',
  'source-to-visual',
  'preserve-page',
  'external-version',
] as const

export type SetContentReason = (typeof ALLOWED_SET_CONTENT_REASONS)[number]

export function assertSetContentReason(reason: string): asserts reason is SetContentReason {
  if (!(ALLOWED_SET_CONTENT_REASONS as readonly string[]).includes(reason)) {
    throw new Error(`setContent reason "${reason}" is not permitted`)
  }
}
