import type { Editor } from '@tiptap/react'
import { normalizeBlockSelection } from './selectionUtils'

/**
 * Apply heading to the current selection only.
 * With a caret (no selection), toggles heading on the whole block (Markdown-normal).
 * With a text selection inside a paragraph, splits so only the selected words become the heading line.
 */
export function applyHeadingToSelection(editor: Editor, level: 1 | 2 | 3) {
  // Trim selection overhang (triple-click grabs the start of the next block).
  normalizeBlockSelection(editor)

  const { empty, $from, $to } = editor.state.selection
  let { from, to } = editor.state.selection

  if (empty) {
    editor.chain().focus().toggleHeading({ level }).run()
    return
  }

  // Multi-block selection: fall back to TipTap's block toggle
  if ($from.parent !== $to.parent || !$from.parent.isTextblock) {
    editor.chain().focus().toggleHeading({ level }).run()
    return
  }

  // Trim whitespace at the selection edges so splits don't leave stray spaces.
  const selText = editor.state.doc.textBetween(from, to, '\n', '\n')
  const leading = selText.length - selText.trimStart().length
  const trailing = selText.length - selText.trimEnd().length
  from += leading
  to -= trailing
  if (from >= to) {
    editor.chain().focus().toggleHeading({ level }).run()
    return
  }

  // Selection covers all real content of the block → toggle the block itself.
  // (Split-and-insert here would leave empty paragraphs behind.)
  if (selText.trim() === $from.parent.textContent.trim()) {
    if (editor.isActive('heading', { level })) {
      editor.chain().focus().setParagraph().run()
    } else {
      editor.chain().focus().toggleHeading({ level }).run()
    }
    return
  }

  const selectedSlice = editor.state.doc.slice(from, to)
  const headingType = editor.schema.nodes.heading
  if (!headingType) {
    editor.chain().focus().toggleHeading({ level }).run()
    return
  }

  editor
    .chain()
    .focus()
    .command(({ tr, dispatch }) => {
      if (!dispatch) return true

      // Delete selection, then insert a heading node with that content
      tr.delete(from, to)

      const content = selectedSlice.content
      const insertPos = from
      try {
        const headingNode = headingType.create({ level }, content.size ? content : undefined)
        tr.insert(insertPos, headingNode)
      } catch {
        // Fallback: insert as text in a heading
        const text = selectedSlice.content.textBetween(0, selectedSlice.content.size, ' ')
        const headingNode = headingType.create(
          { level },
          text ? editor.schema.text(text) : undefined,
        )
        tr.insert(insertPos, headingNode)
      }

      dispatch(tr.scrollIntoView())
      return true
    })
    .run()
}
