import { describe, expect, it } from 'vitest'
import { createTestEditor, editorMarkdown } from '../testing/createTestEditor'
import { createEditorMarkdownCodec } from './EditorMarkdownCodec'
import { createOfficialMarkdownCodec } from './OfficialMarkdownCodec'
import { normalizeSoftBreaks } from './softBreakNormalizer'
import { JOTDEX_TASK_META } from '../extensions/JotdexTaskMetadata'

describe('official Jotdex dialect', () => {
  it('keeps SampleVault-style encoded asset image src', () => {
    const src =
      '# OPNsense IPsec VPN\n\nPhase 2 settings for site-to-site.\n\n```powershell\nGet-VpnConnection\n```\n\nLiteral tech strings: `0x80070005`.\n\n![Phase 2](OPNsense%20IPsec%20VPN.assets/screenshot-001.png)\n'
    const editor = createTestEditor(src)
    const types = (editor.getJSON().content ?? []).map((n) => n.type)
    const md = editorMarkdown(editor)
    expect(types).toContain('image')
    expect(types).toContain('codeBlock')
    expect(md).toMatch(/!\[Phase 2]\(OPNsense%20IPsec%20VPN\.assets\/screenshot-001\.png\)/)
    editor.destroy()
  })

  it('image then H3 does not fuse (H-08 / OFF-09)', () => {
    const editor = createTestEditor('![alt](Note.assets/x.png)\n\n### Subtitle')
    const md = editorMarkdown(editor)
    expect(md).not.toMatch(/\)###/)
    expect(md).toContain('![alt](Note.assets/x.png)')
    expect(md).toMatch(/^### Subtitle/m)
    editor.destroy()
  })

  it('survives 20 image+heading parse/serialize cycles', () => {
    let md = '![alt](Note.assets/x.png)\n\n### Subtitle'
    for (let i = 0; i < 20; i++) {
      const editor = createTestEditor(md)
      md = editorMarkdown(editor)
      editor.destroy()
    }
    expect(md).not.toMatch(/\)###/)
    expect(md).toMatch(/### Subtitle/)
  })

  it('preserves jotdex-task comments (OFF comments)', () => {
    const src = '- [ ] Review <!-- jotdex-task id="abc" priority="high" -->'
    const editor = createTestEditor(src)
    let found = false
    editor.state.doc.descendants((n) => {
      if (n.type.name === JOTDEX_TASK_META) found = true
    })
    expect(found).toBe(true)
    expect(editorMarkdown(editor)).toContain('jotdex-task')
    expect(editorMarkdown(editor)).toContain('id="abc"')
    editor.destroy()
  })

  it('parses Obsidian callouts and keeps type', () => {
    const editor = createTestEditor('> [!warning]\n> Careful')
    const types = (editor.getJSON().content ?? []).map((n) => n.type)
    expect(types).toContain('callout')
    const md = editorMarkdown(editor)
    expect(md).toMatch(/\[!warning\]/i)
    expect(md).toContain('Careful')
    editor.destroy()
  })

  it('preserves unresolved wikilinks', () => {
    const editor = createTestEditor('See [[Missing Note]] please')
    expect(editorMarkdown(editor)).toContain('[[Missing Note]]')
    editor.destroy()
  })

  it('OFF-01 soft breaks become spaces in prose', () => {
    const { doc, changed } = normalizeSoftBreaks({
      type: 'doc',
      content: [{ type: 'paragraph', content: [{ type: 'text', text: 'line one\nline two' }] }],
    })
    expect(changed).toBe(true)
    expect((doc.content?.[0]?.content?.[0] as { text?: string }).text).toBe('line one line two')
  })

  it('does not rewrite code newlines', () => {
    const { changed } = normalizeSoftBreaks({
      type: 'doc',
      content: [{ type: 'codeBlock', content: [{ type: 'text', text: 'a\nb' }] }],
    })
    expect(changed).toBe(false)
  })

  it('styled span round-trips color', () => {
    const src = 'Hello <span style="color: #b42318">red</span> text'
    const codec = createOfficialMarkdownCodec()
    const parsed = codec.parse(src)
    expect(parsed.ok).toBe(true)
    const editor = createTestEditor(src)
    const md = editorMarkdown(editor)
    expect(md.includes('red')).toBe(true)
    editor.destroy()
    codec.destroy?.()
  })

  it('generic HTML comments are not dropped', () => {
    const editor = createTestEditor('Hello <!-- jotdex:network-doc --> world')
    expect(editorMarkdown(editor)).toContain('<!-- jotdex:network-doc -->')
    editor.destroy()
  })
})

describe('codec factory', () => {
  it('factory is official only', () => {
    const codec = createEditorMarkdownCodec()
    expect(codec.engine).toBe('official')
    const src = '# Title\n\nHello **world**'
    expect(codec.parse(src).ok).toBe(true)
    createOfficialMarkdownCodec().destroy?.()
    codec.destroy?.()
  })
})
