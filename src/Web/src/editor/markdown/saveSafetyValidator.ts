import type { Node as PmNode } from '@tiptap/pm/model'
import { PENDING_ASSET_NODE } from '../extensions/PendingAssetPlaceholder'

export type EditorDiagnostic = {
  code: string
  severity: 'warning' | 'error'
  message: string
  markdownLine?: number
  operationId?: string
}

const TRANSIENT_SRC = [
  /https:\/\/paste\.invalid\//i,
  /\bblob:/i,
  /data:image\//i,
  /\/api\/attachments\//i,
  /jotdex-pending:/i,
]

const FUSED_BLOCK =
  /!\[[^\]]*\]\([^)]+\)#{1,6}\s|!\[[^\]]*\]\([^)]+\)[-*+]\s|!\[[^\]]*\]\([^)]+\)\d+\.\s|!\[[^\]]*\]\([^)]+\)```|!\[[^\]]*\]\([^)]+\)>/

export function validateMarkdownSafety(markdown: string, doc?: PmNode): EditorDiagnostic[] {
  const diagnostics: EditorDiagnostic[] = []
  const lines = markdown.split('\n')

  if (doc) {
    doc.descendants((node) => {
      if (node.type.name === PENDING_ASSET_NODE) {
        diagnostics.push({
          code: 'pending-placeholder',
          severity: 'error',
          message: 'Pending upload placeholder must not be saved',
        })
      }
      if (node.type.name === 'image') {
        const src = String(node.attrs.src ?? '')
        for (const re of TRANSIENT_SRC) {
          if (re.test(src)) {
            diagnostics.push({
              code: 'transient-image-src',
              severity: 'error',
              message: `Image src is not persistable: ${re.source}`,
            })
          }
        }
      }
    })
  }

  let inFence = false
  lines.forEach((line, i) => {
    if (line.trimStart().startsWith('```')) {
      inFence = !inFence
      return
    }
    if (inFence) return
    for (const re of TRANSIENT_SRC) {
      if (re.test(line)) {
        diagnostics.push({
          code: 'transient-url',
          severity: 'error',
          message: 'Transient or internal asset URL must not be saved',
          markdownLine: i + 1,
        })
      }
    }
    if (FUSED_BLOCK.test(line)) {
      diagnostics.push({
        code: 'fused-block-boundary',
        severity: 'error',
        message: 'Block image is fused to a following Markdown marker',
        markdownLine: i + 1,
      })
    }
  })

  const fenceCount = (markdown.match(/^```/gm) ?? []).length
  if (fenceCount % 2 !== 0) {
    diagnostics.push({
      code: 'unbalanced-fence',
      severity: 'error',
      message: 'Fenced code blocks are unbalanced',
    })
  }

  if (/\[table\]/i.test(markdown)) {
    diagnostics.push({
      code: 'serializer-fallback',
      severity: 'error',
      message: 'Serializer wrote an unsupported [table] fallback',
    })
  }

  return diagnostics.filter((d) => d.severity === 'error' || d.severity === 'warning')
}

export function isSaveSafe(markdown: string, doc?: PmNode): boolean {
  return validateMarkdownSafety(markdown, doc).every((d) => d.severity !== 'error')
}
