import type { JSONContent } from '@tiptap/core'
import { liftStandaloneImages, trimTrailingEmptyParagraph } from './liftBlockImages'
import { normalizeSoftBreaks } from './softBreakNormalizer'
import { foldAlignMarkers } from '../formatting/alignment'
import type { EditorDiagnostic } from './saveSafetyValidator'

const CALLOUT_TYPES = new Set(['note', 'tip', 'warning', 'info', 'danger'])

function nodeText(node: JSONContent): string {
  if (node.type === 'text') return String(node.text ?? '')
  return (node.content ?? []).map(nodeText).join('')
}

function stripCalloutMarker(node: JSONContent): JSONContent {
  if (node.type === 'text' && node.text) {
    return { ...node, text: node.text.replace(/^\s*\[!\w+\][+-]?(?:\s+[^\n]*)?/i, '') }
  }
  if (!node.content) return node
  return { ...node, content: node.content.map(stripCalloutMarker) }
}

export function promoteCalloutBlockquotes(doc: JSONContent): { doc: JSONContent; changed: boolean } {
  let changed = false
  const content = (doc.content ?? []).map((node) => {
    if (node.type !== 'blockquote') return node
    const text = nodeText(node).trimStart()
    const m = /^\[!(\w+)\]([+-])?(?:\s+([^\n]*))?/i.exec(text)
    const type = m?.[1]?.toLowerCase()
    if (!type || !CALLOUT_TYPES.has(type)) return node
    changed = true
    const collapse = m?.[2] === '-' ? 'collapsed' : m?.[2] === '+' ? 'expanded' : null
    const title = (m?.[3] ?? '').trim()
    const inner = (node.content ?? [])
      .map(stripCalloutMarker)
      .filter((c) => nodeText(c).trim().length > 0 || c.type !== 'paragraph')
    return {
      type: 'callout',
      attrs: { type, title, collapse },
      content: inner.length ? inner : [{ type: 'paragraph' }],
    }
  })
  return { doc: { ...doc, content }, changed }
}

export function applyOfficialParseFixes(doc: JSONContent): { doc: JSONContent; diagnostics: EditorDiagnostic[] } {
  const diagnostics: EditorDiagnostic[] = []
  let next = liftStandaloneImages(doc).doc
  next = promoteCalloutBlockquotes(next).doc
  next = foldAlignMarkers(next).doc
  next = trimTrailingEmptyParagraph(next).doc
  const soft = normalizeSoftBreaks(next)
  next = soft.doc
  if (soft.changed) {
    diagnostics.push({
      code: 'soft-break-normalized',
      severity: 'warning',
      message: 'Soft line breaks were normalized to spaces',
    })
  }
  return { doc: next, diagnostics }
}
