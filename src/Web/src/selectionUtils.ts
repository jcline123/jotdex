import type { Editor } from '@tiptap/react'
import { TextSelection } from '@tiptap/pm/state'

/**
 * Triple-click and drag selections often overhang block boundaries: the end
 * lands at the very start of the next paragraph (offset 0) or the start sits at
 * the tail of the previous one. Block commands (task list, bullet list, heading)
 * then convert neighbouring blocks the user never meant to touch.
 *
 * This shrinks the selection so it only spans blocks that actually contain
 * selected text. No-op for carets, node selections, and already-tight ranges.
 */
export function normalizeBlockSelection(editor: Editor): void {
  const { state } = editor
  const sel = state.selection
  if (sel.empty || !(sel instanceof TextSelection)) return

  const { doc } = state
  let from = sel.from
  let to = sel.to

  // End overhangs at the very start of a following textblock → pull it back.
  let $to = doc.resolve(to)
  while (to > from && $to.parent.isTextblock && $to.parentOffset === 0) {
    to = $to.before()
    $to = doc.resolve(to)
  }

  // Start overhangs at the very end of a previous non-empty textblock → push it forward.
  let $from = doc.resolve(from)
  while (
    from < to &&
    $from.parent.isTextblock &&
    $from.parent.content.size > 0 &&
    $from.parentOffset === $from.parent.content.size
  ) {
    from = $from.after()
    $from = doc.resolve(from)
  }

  if (from >= to) return

  // Snap to valid text positions (before()/after() can land between blocks).
  const next = TextSelection.between(doc.resolve(from), doc.resolve(to), -1)
  if (next.from === sel.from && next.to === sel.to) return
  if (next.from >= next.to) return
  editor.view.dispatch(state.tr.setSelection(next))
}
