import type { Editor } from '@tiptap/core'
import { Fragment } from '@tiptap/pm/model'
import { TextSelection } from '@tiptap/pm/state'
import { normalizeBlockSelection } from './selectionUtils'
import { newOperationId, setOperationMeta } from './editor/operations/operationMeta'

const NESTED_BLOCKS = new Set([
  'listItem',
  'taskItem',
  'tableCell',
  'tableHeader',
  'blockquote',
  'callout',
  'codeBlock',
])

function nestedContextName($from: { depth: number; node: (d: number) => { type: { name: string } } }): string | null {
  for (let depth = $from.depth; depth > 0; depth--) {
    const name = $from.node(depth).type.name
    if (NESTED_BLOCKS.has(name)) return name
  }
  return null
}

export type HeadingResult = { ok: boolean; reason?: string }

/**
 * Apply heading to the current selection.
 * Partial selection in a top-level paragraph replaces the whole block with
 * left remainder + heading + right remainder (never insert a block at an inline pos).
 */
export function applyHeadingToSelection(editor: Editor, level: 1 | 2 | 3 | 4 | 5 | 6): HeadingResult {
  normalizeBlockSelection(editor)

  const { empty, $from, $to } = editor.state.selection
  const nested = nestedContextName($from)
  if (nested && !empty && $from.parent !== $to.parent) {
    return { ok: false, reason: `Cannot split a heading across ${nested}` }
  }
  if (nested && !empty && $from.parent === $to.parent) {
    const parent = $from.parent
    const all = parent.textContent.trim() === editor.state.doc.textBetween($from.pos, $to.pos, '\n', '\n').trim()
    if (!all) {
      return {
        ok: false,
        reason: `Partial headings inside ${nested} cannot be stored as Markdown. Select the whole block or use Source.`,
      }
    }
    editor.chain().focus().toggleHeading({ level }).run()
    return { ok: true }
  }

  if (empty) {
    editor.chain().focus().toggleHeading({ level }).run()
    return { ok: true }
  }

  if ($from.parent !== $to.parent || !$from.parent.isTextblock) {
    editor.chain().focus().toggleHeading({ level }).run()
    return { ok: true }
  }

  const { from, to } = editor.state.selection
  const parent = $from.parent
  const blockPos = $from.before()
  const contentStart = $from.start()
  const leftSize = from - contentStart
  const rightStart = to - contentStart

  if (leftSize <= 0 && rightStart >= parent.content.size) {
    if (editor.isActive('heading', { level })) {
      editor.chain().focus().setParagraph().run()
    } else {
      editor.chain().focus().toggleHeading({ level }).run()
    }
    return { ok: true }
  }

  const headingType = editor.schema.nodes.heading
  const paragraphType = editor.schema.nodes.paragraph
  if (!headingType || !paragraphType) {
    editor.chain().focus().toggleHeading({ level }).run()
    return { ok: true }
  }

  const leftContent = parent.content.cut(0, Math.max(0, leftSize))
  const midContent = parent.content.cut(Math.max(0, leftSize), Math.min(parent.content.size, rightStart))
  const rightContent = parent.content.cut(Math.min(parent.content.size, rightStart), parent.content.size)

  const nodes = []
  if (leftContent.size > 0) nodes.push(parent.type.create(parent.attrs, leftContent))
  nodes.push(headingType.create({ level }, midContent.size ? midContent : undefined))
  if (rightContent.size > 0) nodes.push(paragraphType.create(null, rightContent))

  const { state } = editor
  let tr = state.tr.replaceWith(blockPos, blockPos + parent.nodeSize, Fragment.from(nodes))
  const headingNode = nodes[leftContent.size > 0 ? 1 : 0]
  const headingStart = blockPos + (leftContent.size > 0 ? nodes[0]!.nodeSize : 0) + 1
  const headingEnd = headingStart + (headingNode?.content.size ?? 0)
  try {
    tr = tr.setSelection(TextSelection.create(tr.doc, headingStart, Math.max(headingStart, headingEnd)))
  } catch {
    /* keep mapped selection */
  }
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'heading',
    serializable: true,
    commitBoundary: true,
  })
  editor.view.dispatch(tr.scrollIntoView())
  return { ok: true }
}
