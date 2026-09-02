import type { Editor } from '@tiptap/core'
import { newOperationId, setOperationMeta } from '../operations/operationMeta'

export type LiveOutlineItem = {
  level: number
  text: string
  pos: number
  slug: string
}

export function slugifyHeading(text: string): string {
  return text
    .trim()
    .toLowerCase()
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^\w\s-]/g, '')
    .replace(/\s+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '')
}

export function extractLiveOutline(doc: {
  descendants: (fn: (node: { type: { name: string }; attrs: { level?: number }; textContent: string }, pos: number) => void) => void
}): LiveOutlineItem[] {
  const items: LiveOutlineItem[] = []
  const used = new Map<string, number>()
  doc.descendants((node, pos) => {
    if (node.type.name !== 'heading') return
    const text = node.textContent.trim()
    if (!text) return
    const base = slugifyHeading(text) || 'section'
    const n = used.get(base) ?? 0
    used.set(base, n + 1)
    const slug = n === 0 ? base : `${base}-${n + 1}`
    items.push({ level: Number(node.attrs.level ?? 1), text, pos, slug })
  })
  return items
}

export function sectionRange(editor: Editor, headingPos: number): { from: number; to: number } | null {
  const heading = editor.state.doc.nodeAt(headingPos)
  if (!heading || heading.type.name !== 'heading') return null
  const level = Number(heading.attrs.level ?? 1)
  let pos = headingPos + heading.nodeSize
  while (pos < editor.state.doc.content.size) {
    const node = editor.state.doc.nodeAt(pos)
    if (!node) break
    if (node.type.name === 'heading' && Number(node.attrs.level ?? 1) <= level) break
    pos += node.nodeSize
  }
  return { from: headingPos, to: pos }
}

export function moveSection(editor: Editor, headingPos: number, dir: -1 | 1): boolean {
  const range = sectionRange(editor, headingPos)
  if (!range) return false
  const node = editor.state.doc.slice(range.from, range.to)
  const size = range.to - range.from
  if (dir < 0) {
    if (range.from === 0) return false
    const $before = editor.state.doc.resolve(range.from)
    const prev = $before.nodeBefore
    if (!prev) return false
    const insertPos = range.from - prev.nodeSize
    const tr = editor.state.tr.delete(range.from, range.to).insert(insertPos, node.content)
    setOperationMeta(tr, {
      operationId: newOperationId(),
      kind: 'block-move',
      serializable: true,
      commitBoundary: true,
    })
    editor.view.dispatch(tr.scrollIntoView())
    return true
  }
  const afterPos = range.to
  if (afterPos >= editor.state.doc.content.size) return false
  const next = editor.state.doc.nodeAt(afterPos)
  if (!next) return false
  const insertPos = afterPos + next.nodeSize - size
  const tr = editor.state.tr.delete(range.from, range.to).insert(insertPos, node.content)
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'block-move',
    serializable: true,
    commitBoundary: true,
  })
  editor.view.dispatch(tr.scrollIntoView())
  return true
}
