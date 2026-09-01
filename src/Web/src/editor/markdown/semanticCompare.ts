import type { Node as PmNode } from '@tiptap/pm/model'

const ATTR_KEYS = ['level', 'language', 'src', 'alt', 'title', 'href', 'checked', 'type'] as const

function meaningfulAttrs(node: PmNode): Record<string, unknown> {
  const out: Record<string, unknown> = {}
  for (const key of ATTR_KEYS) {
    if (node.attrs[key] != null && node.attrs[key] !== false && node.attrs[key] !== '') {
      out[key] = node.attrs[key]
    }
  }
  return out
}

function marksKey(node: PmNode): string {
  if (!node.marks?.length) return ''
  return node.marks
    .map((m) => {
      const href = m.attrs.href ? `@${m.attrs.href}` : ''
      const extra = m.attrs.color || m.attrs.fontSize ? JSON.stringify(m.attrs) : ''
      return `${m.type.name}${href}${extra}`
    })
    .sort()
    .join(',')
}

export function semanticFingerprint(node: PmNode): string {
  const parts: string[] = [`${node.type.name}:${JSON.stringify(meaningfulAttrs(node))}:${marksKey(node)}`]
  if (node.isText) parts.push(JSON.stringify(node.text ?? ''))
  node.forEach((child) => {
    parts.push(semanticFingerprint(child))
  })
  return parts.join('|')
}

export type SemanticComparison = {
  equal: boolean
  a: string
  b: string
}

export function compareSemantic(a: PmNode, b: PmNode): SemanticComparison {
  const fa = semanticFingerprint(a)
  const fb = semanticFingerprint(b)
  return { equal: fa === fb, a: fa, b: fb }
}

export function blockFingerprints(doc: PmNode): string[] {
  const out: string[] = []
  doc.forEach((child) => out.push(semanticFingerprint(child)))
  return out
}
