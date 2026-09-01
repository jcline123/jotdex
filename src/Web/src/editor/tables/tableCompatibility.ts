import type { JSONContent } from '@tiptap/core'

const CONTROL = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/

export function tableHasControlCharacters(markdown: string): boolean {
  return CONTROL.test(markdown) && markdown.includes('|')
}

export function cellHasMultipleBlocks(cell: JSONContent): boolean {
  const kids = cell.content ?? []
  const blocks = kids.filter((k) => k.type && k.type !== 'text')
  return blocks.length > 1
}

export function documentHasMultiBlockTableCell(doc: JSONContent): boolean {
  const walk = (node: JSONContent): boolean => {
    if ((node.type === 'tableCell' || node.type === 'tableHeader') && cellHasMultipleBlocks(node)) {
      return true
    }
    return (node.content ?? []).some(walk)
  }
  return walk(doc)
}
