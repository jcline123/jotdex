import { describe, expect, it } from 'vitest'
import { createTestEditor, editorMarkdown } from '../testing/createTestEditor'
import { validateMarkdownSafety } from './saveSafetyValidator'
import { tableHasControlCharacters } from '../tables/tableCompatibility'

describe('official engine guards OFF-01–OFF-10', () => {
  it('OFF-01 soft break in one paragraph', () => {
    const editor = createTestEditor('line one\nline two')
    expect(editor.state.doc.textContent).toContain('line one')
    editor.destroy()
  })

  it('OFF-02 table control characters are flagged', () => {
    expect(tableHasControlCharacters('| A |\n| -- |\n| \u0001 |')).toBe(true)
  })

  it('OFF-03 comment inside heading does not crash', () => {
    const editor = createTestEditor('# Title <!-- keep -->')
    expect(editor.state.doc.firstChild?.type.name).toBe('heading')
    editor.destroy()
  })

  it('OFF-04 heading after ordered list', () => {
    const editor = createTestEditor('1. item\n\n## Next')
    const md = editorMarkdown(editor)
    expect(md).toMatch(/## Next/)
    editor.destroy()
  })

  it('OFF-05 task list after paragraph', () => {
    const editor = createTestEditor('Intro\n\n- [ ] Task')
    expect(editorMarkdown(editor)).toMatch(/\[ \]/)
    editor.destroy()
  })

  it('OFF-06 hard break two spaces', () => {
    const editor = createTestEditor('a  \nb')
    expect(editor.state.doc.textContent.length).toBeGreaterThan(1)
    editor.destroy()
  })

  it('OFF-07 blank line after heading', () => {
    const editor = createTestEditor('## H\n\npara')
    expect(editorMarkdown(editor)).toMatch(/para/)
    editor.destroy()
  })

  it('OFF-09 block image then heading', () => {
    const editor = createTestEditor('![x](a.png)\n\n### H')
    expect(editorMarkdown(editor)).not.toMatch(/\)###/)
    editor.destroy()
  })

  it('OFF-10 unknown persistent node does not serialize empty fused image', () => {
    expect(validateMarkdownSafety('![x](a.png)### H\n').some((d) => d.code === 'fused-block-boundary')).toBe(true)
  })
})
