import type { JSONContent } from '@tiptap/core'

const CODE_PARENTS = new Set(['codeBlock'])

function hasCodeMark(node: JSONContent): boolean {
  return (node.marks ?? []).some((m) => m.type === 'code')
}

/** Replace CommonMark soft newlines in prose text nodes with a space. Skips code. */
export function normalizeSoftBreaks(doc: JSONContent): { doc: JSONContent; changed: boolean } {
  let changed = false

  const walk = (node: JSONContent, parentType: string | undefined): JSONContent => {
    if (node.type === 'hardBreak') return node
    if (node.type === 'text' && typeof node.text === 'string' && node.text.includes('\n')) {
      if (parentType && CODE_PARENTS.has(parentType)) return node
      if (hasCodeMark(node)) return node
      changed = true
      return { ...node, text: node.text.replace(/\n/g, ' ') }
    }
    if (!node.content) return node
    return {
      ...node,
      content: node.content.map((child) => walk(child, node.type)),
    }
  }

  return { doc: walk(doc, undefined), changed }
}
