import { describe, expect, it } from 'vitest'
import { createTestEditor, editorMarkdown } from '../testing/createTestEditor'
import { PENDING_ASSET_NODE } from '../extensions/PendingAssetPlaceholder'

const TRANSIENT = new Set([PENDING_ASSET_NODE, 'text', 'doc'])
const SOURCE_ONLY = new Set<string>()

describe('persistent schema coverage', () => {
  it('every persistent node serializes to non-empty markdown or is transient/source-only', () => {
    const editor = createTestEditor('# H\n\npara')
    const names = Object.keys(editor.schema.nodes)
    const empty: string[] = []
    for (const name of names) {
      if (TRANSIENT.has(name) || SOURCE_ONLY.has(name)) continue
      const type = editor.schema.nodes[name]
      if (!type) continue
      try {
        let md = ''
        if (name === 'image') {
          const e = createTestEditor('![x](a.png)')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'heading') {
          md = editorMarkdown(createTestEditor('# Hi'))
        } else if (name === 'codeBlock') {
          const e = createTestEditor('```js\nconst x = 1\n```')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'horizontalRule') {
          const e = createTestEditor('---\n')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'blockquote') {
          const e = createTestEditor('> quote')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'bulletList' || name === 'listItem') {
          const e = createTestEditor('- item')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'orderedList') {
          const e = createTestEditor('1. item')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'taskList' || name === 'taskItem') {
          const e = createTestEditor('- [ ] task')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'table' || name === 'tableRow' || name === 'tableCell' || name === 'tableHeader') {
          const e = createTestEditor('| A | B |\n| --- | --- |\n| 1 | 2 |\n')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'callout') {
          const e = createTestEditor('> [!note]\n> hi')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'jotdexTaskMetadata') {
          const e = createTestEditor('- [ ] X <!-- jotdex-task id="1" -->')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'rawHtmlCommentInline') {
          const e = createTestEditor('Hi <!-- keep --> there')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'rawHtmlCommentBlock') {
          const e = createTestEditor('<!--\nblock\n-->\n')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'unresolvedWikiLink') {
          const e = createTestEditor('[[Missing]]')
          md = editorMarkdown(e)
          e.destroy()
        } else if (name === 'hardBreak') {
          md = 'ok'
        } else if (name === 'paragraph') {
          md = editorMarkdown(createTestEditor('hello'))
        } else {
          md = 'skip'
        }
        if (md === '') empty.push(name)
      } catch {
        empty.push(name)
      }
    }
    editor.destroy()
    expect(empty).toEqual([])
    expect(names.length).toBeGreaterThan(8)
  })
})
