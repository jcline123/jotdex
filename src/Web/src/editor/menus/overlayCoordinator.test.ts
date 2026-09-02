import { describe, expect, it } from 'vitest'
import { OverlayCoordinator } from './overlayCoordinator'
import { captureSelectionBookmark, restoreSelectionBookmark } from './selectionBookmark'
import { createTestEditor, findTextRange } from '../testing/createTestEditor'

describe('overlay coordinator', () => {
  it('keeps one primary menu and Escape closes it', () => {
    const overlays = new OverlayCoordinator()
    overlays.open('slash')
    expect(overlays.blocksBubble).toBe(true)
    overlays.open('plus')
    expect(overlays.primary).toBe('plus')
    expect(overlays.handleEscape()).toBe(true)
    expect(overlays.primary).toBe('none')
    expect(overlays.handleEscape()).toBe(false)
  })
})

describe('selection bookmarks', () => {
  it('restores only when note id and session match', () => {
    const editor = createTestEditor('Hello world')
    const range = findTextRange(editor, 'world')
    editor.chain().setTextSelection(range).run()
    const bookmark = captureSelectionBookmark(editor, 'note-a', 'session-1')
    expect(restoreSelectionBookmark(editor, bookmark, 'note-b', 'session-1')).toBeNull()
    expect(restoreSelectionBookmark(editor, bookmark, 'note-a', 'session-2')).toBeNull()
    const restored = restoreSelectionBookmark(editor, bookmark, 'note-a', 'session-1')
    expect(restored?.from).toBe(bookmark.from)
    editor.destroy()
  })
})
