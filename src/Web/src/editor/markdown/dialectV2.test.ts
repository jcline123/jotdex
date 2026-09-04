import { describe, expect, it } from 'vitest'
import { createTestEditor, editorMarkdown, reopenMarkdown } from '../testing/createTestEditor'

describe('dialect v2 marks and blocks', () => {
  it('FMT highlight underline sub sup survive 20 cycles', () => {
    let md = 'A ==hi== and <u>under</u> and H<sub>2</sub>O and x<sup>2</sup>.'
    for (let i = 0; i < 20; i++) {
      const editor = createTestEditor(md)
      md = editorMarkdown(editor)
      editor.destroy()
    }
    expect(md).toContain('==hi==')
    expect(md).toMatch(/<u>under<\/u>/i)
    expect(md).toMatch(/<sub>2<\/sub>/i)
    expect(md).toMatch(/<sup>2<\/sup>/i)
  })

  it('details comment dialect round-trips and omits open state', () => {
    const src = '<!-- jotdex-details -->\nSummary here\n\nHidden body\n<!-- /jotdex-details -->\n'
    const { editor, markdown } = reopenMarkdown(src)
    expect(markdown).toContain('<!-- jotdex-details -->')
    expect(markdown).toContain('<!-- /jotdex-details -->')
    expect(markdown).toContain('Summary here')
    expect(markdown).toContain('Hidden body')
    expect(markdown).not.toMatch(/\bopen\b/)
    editor.destroy()
  })

  it('alignment comment stays on the following paragraph', () => {
    const src = '<!-- jotdex-align: center -->\nCentered heading text\n'
    const editor = createTestEditor(src)
    const md = editorMarkdown(editor)
    expect(md).toContain('<!-- jotdex-align: center -->')
    expect(md).toContain('Centered heading text')
    editor.destroy()
  })

  it('math uses paren delimiters and never dollars', () => {
    const editor = createTestEditor('See \\(a+b\\) and\n\n\\[x=1\\]\n')
    const md = editorMarkdown(editor)
    expect(md).toContain('\\(a+b\\)')
    expect(md).toContain('\\[x=1\\]')
    expect(md).not.toContain('$')
    editor.destroy()
  })

  it('legacy callout still round-trips', () => {
    const editor = createTestEditor('> [!warning]\n> Careful')
    const md = editorMarkdown(editor)
    expect(md).toMatch(/\[!warning\]/)
    expect(md).toContain('Careful')
    editor.destroy()
  })

  it('titled collapsible callout keeps marker title and collapse', () => {
    const editor = createTestEditor('> [!tip]- Watch this\n> Body')
    const md = editorMarkdown(editor)
    expect(md).toMatch(/\[!tip\]-/)
    expect(md).toContain('Watch this')
    expect(md).toContain('Body')
    editor.destroy()
  })

  it('bookmark card dialect', () => {
    const src = '<!-- jotdex-link-card -->\n[Example](https://example.com)\n'
    const editor = createTestEditor(src)
    const md = editorMarkdown(editor)
    expect(md).toContain('jotdex-link-card')
    expect(md).toContain('https://example.com')
    editor.destroy()
  })

  it('standard image stays markdown; figure when caption set', () => {
    const editor = createTestEditor('![alt](Note.assets/x.png)')
    expect(editorMarkdown(editor)).toMatch(/!\[alt\]\(Note\.assets\/x\.png\)/)
    editor.commands.updateAttributes('image', { caption: 'A caption', width: '320' })
    const md = editorMarkdown(editor)
    expect(md).toContain('<figure')
    expect(md).toContain('A caption')
    expect(md).not.toMatch(/blob:/)
    editor.destroy()
  })

  it('figure width percent round-trips', () => {
    const editor = createTestEditor('![alt](Note.assets/x.png)')
    editor.commands.updateAttributes('image', { width: '65%' })
    const md = editorMarkdown(editor)
    expect(md).toContain('width="65%"')
    editor.destroy()
    const again = createTestEditor(md)
    expect(editorMarkdown(again)).toContain('width="65%"')
    again.destroy()
  })
})
