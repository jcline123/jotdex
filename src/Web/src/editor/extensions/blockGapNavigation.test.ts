import { describe, expect, it } from 'vitest'
import type { Editor } from '@tiptap/core'
import { GapCursor } from '@tiptap/pm/gapcursor'
import { NodeSelection, TextSelection } from '@tiptap/pm/state'
import { createTestEditor, editorMarkdown } from '../testing/createTestEditor'

function pressKey(editor: Editor, key: string, opts: KeyboardEventInit = {}): boolean {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...opts })
  return Boolean(editor.view.someProp('handleKeyDown', (handler) => handler(editor.view, event)))
}

function adjacentCodeDoc() {
  return {
    type: 'doc' as const,
    content: [
      {
        type: 'codeBlock',
        attrs: { language: 'plaintext' },
        content: [{ type: 'text', text: 'one' }],
      },
      {
        type: 'codeBlock',
        attrs: { language: 'plaintext' },
        content: [{ type: 'text', text: 'two' }],
      },
    ],
  }
}

function endOfFirstCode(editor: Editor): number {
  const node = editor.state.doc.child(0)
  expect(node.type.name).toBe('codeBlock')
  return node.nodeSize - 1
}

describe('block gap navigation', () => {
  it('marks code blocks so a gap cursor is valid between two of them', () => {
    const editor = createTestEditor()
    editor.commands.setContent(adjacentCodeDoc())
    expect((editor.schema.nodes.codeBlock?.spec as { createGapCursor?: boolean }).createGapCursor).toBe(
      true,
    )
    const gap = editor.state.doc.child(0).nodeSize
    expect(GapCursor.valid(editor.state.doc.resolve(gap))).toBe(true)
    editor.destroy()
  })

  it('ArrowDown at the end of a code box lands in the gap before the next box', () => {
    const editor = createTestEditor()
    editor.commands.setContent(adjacentCodeDoc())
    editor.commands.setTextSelection(endOfFirstCode(editor))
    expect(editor.commands.arrowToBlockGap(1)).toBe(true)
    expect(editor.state.selection).toBeInstanceOf(GapCursor)
    expect(editor.state.selection.from).toBe(editor.state.doc.child(0).nodeSize)
    editor.destroy()
  })

  it('Enter on that gap inserts a paragraph you can type in', () => {
    const editor = createTestEditor()
    editor.commands.setContent(adjacentCodeDoc())
    editor.commands.setTextSelection(endOfFirstCode(editor))
    editor.commands.arrowToBlockGap(1)
    expect(editor.commands.insertParagraphAtBlockGap()).toBe(true)
    const types = Array.from(
      { length: editor.state.doc.childCount },
      (_, i) => editor.state.doc.child(i).type.name,
    )
    expect(types.join(',')).toMatch(/^codeBlock,paragraph,codeBlock(?:,paragraph)?$/)
    expect(editor.state.selection).toBeInstanceOf(TextSelection)
    const md = editorMarkdown(editor)
    expect(md).toMatch(/```[\s\S]*one[\s\S]*```\s*\n\s*\n[\s\S]*```[\s\S]*two/)
    editor.destroy()
  })

  it('does not steal ArrowDown when the next node is a paragraph', () => {
    const editor = createTestEditor('```\none\n```\n\nhello\n')
    const first = editor.state.doc.child(0)
    expect(first.type.name).toBe('codeBlock')
    editor.commands.setTextSelection(first.nodeSize - 1)
    expect(editor.commands.arrowToBlockGap(1)).toBe(false)
    expect(editor.state.selection.$from.parent.type.name).toBe('codeBlock')
    editor.destroy()
  })

  it('ArrowDown from a selected image lands in the gap before a following code box', () => {
    const editor = createTestEditor()
    editor.commands.setContent({
      type: 'doc',
      content: [
        { type: 'image', attrs: { src: 'a.png', alt: 'pic' } },
        {
          type: 'codeBlock',
          attrs: { language: 'plaintext' },
          content: [{ type: 'text', text: 'after' }],
        },
      ],
    })
    const image = editor.state.doc.child(0)
    expect(image.type.name).toBe('image')
    editor.view.dispatch(editor.state.tr.setSelection(NodeSelection.create(editor.state.doc, 0)))
    expect(editor.commands.arrowToBlockGap(1)).toBe(true)
    expect(editor.state.selection).toBeInstanceOf(GapCursor)
    expect(editor.state.selection.from).toBe(image.nodeSize)
    editor.destroy()
  })

  it('Ctrl/Cmd-Enter inside a code box inserts a line when the next block is another code box', () => {
    const editor = createTestEditor()
    editor.commands.setContent(adjacentCodeDoc())
    editor.commands.setTextSelection(2)
    expect(pressKey(editor, 'Enter', { ctrlKey: true })).toBe(true)
    expect(editor.state.doc.child(1).type.name).toBe('paragraph')
    editor.destroy()
  })
})
