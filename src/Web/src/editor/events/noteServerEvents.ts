export type AttachmentInfo = { id: string; fileName: string; contentType: string }

export type NoteServerEvent =
  | { kind: 'attachments-updated'; attachments: AttachmentInfo[] }
  | { kind: 'etag-confirmed'; etag: string; revision?: number }
  | {
      kind: 'replace-document'
      markdown: string
      etag: string
      reason: 'preserve-page' | 'history-restore' | 'external-version' | 'user-reload'
    }
