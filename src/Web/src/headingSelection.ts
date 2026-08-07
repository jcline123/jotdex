import type { Editor } from '@tiptap/react'

/**
 * Apply heading to the current selection only.
 * With a caret (no selection), toggles heading on the whole block (Markdown-normal).
 * With a text selection inside a paragraph, splits so only the selected words become the heading line.
 */
export function applyHeadingToSelection(editor: Editor, level: 1 | 2 | 3) {
  const { empty, from, to, $from, $to } = editor.state.selection

  if (empty) {
    editor.chain().focus().toggleHeading({ level }).run()
    return
  }

  // Multi-block selection: fall back to TipTap's block toggle
  if ($from.parent !== $to.parent || !$from.parent.isTextblock) {
    editor.chain().focus().toggleHeading({ level }).run()
    return
  }

  // Already a heading of this level covering the selection → turn back into paragraph
  if (editor.isActive('heading', { level }) && from === $from.start() && to === $from.end()) {
    editor.chain().focus().setParagraph().run()
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
      // If slice is inline-only, wrap as heading; if it already has blocks, set type on first
      let insertPos = from
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
