import type { AttachmentInfo } from './editor/assets/AttachmentResolver'

/** Trigger a browser download for a vault attachment (original filename via Content-Disposition). */
export function downloadAttachmentById(attachmentId: string, fileName?: string): void {
  const a = document.createElement('a')
  a.href = `/api/attachments/${encodeURIComponent(attachmentId)}?download=1`
  if (fileName) a.download = fileName
  a.rel = 'noopener'
  a.style.display = 'none'
  document.body.appendChild(a)
  a.click()
  a.remove()
}

/** Resolve a Markdown href (vault-relative or /api/attachments/…) to an attachment and download it. */
export function downloadAttachmentHref(
  href: string,
  attachments: AttachmentInfo[],
): boolean {
  const id = resolveAttachmentId(href, attachments)
  if (!id) return false
  const meta = attachments.find((a) => a.id === id)
  downloadAttachmentById(id, meta?.fileName)
  return true
}

export function resolveAttachmentId(href: string, attachments: AttachmentInfo[]): string | null {
  const raw = (href || '').trim()
  if (!raw) return null
  const api = /^\/api\/attachments\/([^/?#]+)/i.exec(raw)
  if (api?.[1]) return decodeURIComponent(api[1])

  const decoded = safeDecode(raw.split('?')[0] ?? raw)
  const slash = decoded.replace(/\\/g, '/')
  const fileName = slash.split('/').pop() ?? ''
  if (!fileName) return null

  const byPath = attachments.find((a) => {
    const rel = (a as AttachmentInfo & { relativePath?: string }).relativePath?.replace(/\\/g, '/')
    if (rel && (slash.endsWith(rel) || rel.endsWith(slash))) return true
    return a.fileName === fileName || safeDecode(a.fileName) === fileName
  })
  return byPath?.id ?? null
}

export function formatByteSize(bytes: number | undefined | null): string {
  if (bytes == null || !Number.isFinite(bytes) || bytes < 0) return ''
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(bytes < 10 * 1024 ? 1 : 0)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`
}

function safeDecode(s: string): string {
  try {
    return decodeURIComponent(s.replace(/\+/g, ' '))
  } catch {
    return s
  }
}
