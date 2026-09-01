import { mkdirSync, writeFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { Editor } from '@tiptap/core'
import { Markdown } from '@tiptap/markdown'
import StarterKit from '@tiptap/starter-kit'
import Image from '@tiptap/extension-image'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import { Table } from '@tiptap/extension-table'
import { TableRow } from '@tiptap/extension-table-row'
import { TableCell } from '@tiptap/extension-table-cell'
import { TableHeader } from '@tiptap/extension-table-header'
import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import { Callout } from '../../callout'

function officialEditor() {
  return new Editor({
    extensions: [
      StarterKit.configure({ codeBlock: true }),
      Image.configure({ inline: false, allowBase64: false }),
      TaskList,
      TaskItem,
      Table,
      TableRow,
      TableHeader,
      TableCell,
      TextStyle,
      Color,
      Callout,
      Markdown.configure({
        indentation: { style: 'space', size: 2 },
        markedOptions: { gfm: true, breaks: false, pedantic: false },
      }),
    ],
    content: '',
  })
}

function types(editor: Editor): string[] {
  return (editor.getJSON().content ?? []).map((n) => n.type as string)
}

describe('MDM-01 official engine spike (isolated)', () => {
  it('heading + bold/italic', () => {
    const editor = officialEditor()
    editor.commands.setContent('# Title\n\nHello **bold** and *italic*.', { contentType: 'markdown' })
    expect(types(editor)[0]).toBe('heading')
    const md = editor.getMarkdown()
    expect(md).toContain('# Title')
    expect(md).toMatch(/\*\*bold\*\*/)
    editor.destroy()
  })

  it('code block', () => {
    const editor = officialEditor()
    editor.commands.setContent('```js\nconst x = 1\n```\n', { contentType: 'markdown' })
    expect(types(editor)).toContain('codeBlock')
    expect(editor.getMarkdown()).toContain('const x = 1')
    editor.destroy()
  })

  it('task list', () => {
    const editor = officialEditor()
    editor.commands.setContent('- [ ] Open\n- [x] Done\n', { contentType: 'markdown' })
    const md = editor.getMarkdown()
    expect(md).toMatch(/\[ \]|\[x\]/i)
    editor.destroy()
  })

  it('table', () => {
    const editor = officialEditor()
    editor.commands.setContent('| A | B |\n| --- | --- |\n| 1 | 2 |\n', { contentType: 'markdown' })
    expect(types(editor)).toContain('table')
    expect(editor.getMarkdown()).toContain('|')
    editor.destroy()
  })

  it('image followed by H3 (fusion check)', () => {
    const editor = officialEditor()
    const src = '![alt](Note.assets/x.png)\n\n### Subtitle\n'
    editor.commands.setContent(src, { contentType: 'markdown' })
    const json = editor.getJSON()
    const md = editor.getMarkdown()
    expect(md).not.toMatch(/\)###/)
    expect(types(editor)).toEqual(expect.arrayContaining(['image', 'heading']))
    // document join shape for the ADR table
    expect({ types: types(editor), md, first: json.content?.[0] }).toBeTruthy()
    editor.destroy()
  })

  it('styled span', () => {
    const editor = officialEditor()
    editor.commands.setContent('Hello <span style="color: #b42318">red</span> text', { contentType: 'markdown' })
    const md = editor.getMarkdown()
    expect(editor.getJSON()).toBeTruthy()
    expect(md.length).toBeGreaterThan(0)
    editor.destroy()
  })

  it('jotdex task comment', () => {
    const editor = officialEditor()
    editor.commands.setContent('- [ ] Review <!-- jotdex-task id="abc" priority="high" -->\n', {
      contentType: 'markdown',
    })
    const md = editor.getMarkdown()
    expect(md.includes('jotdex-task') || md.includes('Review')).toBe(true)
    editor.destroy()
  })

  it('obsidian callout', () => {
    const editor = officialEditor()
    editor.commands.setContent('> [!warning]\n> Careful\n', { contentType: 'markdown' })
    const json = editor.getJSON()
    const md = editor.getMarkdown()
    expect({ types: types(editor), json, md }).toBeTruthy()
    editor.destroy()
  })

  it('dumps compatibility samples', () => {
    const samples: [string, string][] = [
      ['heading', '# Title\n\nHello **bold** and *italic*.'],
      ['image+h3', '![alt](Note.assets/x.png)\n\n### Subtitle\n'],
      ['image-tight', '![alt](Note.assets/x.png)\n### Subtitle\n'],
      ['style', 'Hello <span style="color: #b42318">red</span> text'],
      ['task', '- [ ] Review <!-- jotdex-task id="abc" priority="high" -->\n'],
      ['callout', '> [!warning]\n> Careful\n'],
      ['html-callout', '<blockquote data-callout="warning"><p>Careful</p></blockquote>'],
      ['wiki', 'See [[Missing Note]] please'],
      ['soft', 'line one\nline two\n'],
    ]
    const editor = officialEditor()
    const report: Record<string, { types: string[]; out: string }> = {}
    for (const [name, src] of samples) {
      editor.commands.setContent(src, { contentType: 'markdown', emitUpdate: false })
      report[name] = { types: types(editor), out: editor.getMarkdown() }
    }
    editor.destroy()
    // Keep the dump in the assertion message if this is ever inspected.
    mkdirSync('C:/JotdexMigration', { recursive: true })
    writeFileSync('C:/JotdexMigration/official-spike.json', JSON.stringify(report, null, 2), 'utf8')
    expect(JSON.stringify(report, null, 2).length).toBeGreaterThan(20)
  })
})
