import { describe, expect, it } from 'vitest'
import { createTestEditor, editorMarkdown, dumpEditor, findTextRange } from '../testing/createTestEditor'
import { applyHeadingToSelection } from '../../headingSelection'
import { undo as pmUndo, redo as pmRedo } from '@tiptap/pm/history'
import { clipboardToPlainCode, pasteAsCodeBlock, pastePlainIntoCodeBlock } from '../paste/codeClipboard'
import { serializeBlockImage } from '../extensions/serializeBlockImage'
import { validateMarkdownSafety } from '../markdown/saveSafetyValidator'
import { exactSaveEqual } from '../markdown/documentSameness'
import { planEditorReload } from '../reloadPolicy'
import { blockFingerprints } from '../markdown/semanticCompare'
import {
  insertPendingAssetAtSelection,
  runPasteSession,
  rewritePastedImagesToPlaceholders,
} from '../paste/PasteSessionManager'
import { dispatchAttachmentInventory } from '../assets/AttachmentResolver'
import { PENDING_ASSET_NODE } from '../extensions/PendingAssetPlaceholder'
import { EditorRevisionCoordinator } from '../revisions/EditorRevisionCoordinator'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import type { Editor } from '@tiptap/core'

const repoRoot = resolve(process.cwd(), '../..')

function mdState() {
  let out = ''
  return {
    write: (t: string) => {
      out += t
    },
    esc: (t: string) => t.replace(/\\/g, '\\\\').replace(/]/g, '\\]'),
    closeBlock: () => {
      out += '\n\n'
    },
    toString: () => out,
  }
}

function topTypes(editor: Editor): string[] {
  return (editor.getJSON().content ?? []).map((n) => n.type)
}

function topTexts(editor: Editor): string[] {
  return (editor.getJSON().content ?? []).map((n) =>
    (n.content ?? []).map((c) => ('text' in c ? String(c.text ?? '') : '')).join(''),
  )
}

function imageSrcs(editor: Editor): string[] {
  const srcs: string[] = []
  editor.state.doc.descendants((node) => {
    if (node.type.name === 'image') srcs.push(String(node.attrs.src ?? ''))
  })
  return srcs
}

function meaningfulBlocks(editor: Editor) {
  const blocks: { type: string; text: string }[] = []
  editor.state.doc.forEach((child) => {
    if (child.type.name === 'paragraph' && child.content.size === 0) return
    blocks.push({ type: child.type.name, text: child.textContent })
  })
  return blocks
}

function pendingCount(editor: Editor): number {
  let n = 0
  editor.state.doc.descendants((node) => {
    if (node.type.name === PENDING_ASSET_NODE) n++
  })
  return n
}

describe('block image serializer', () => {
  it('closes the block so a following heading cannot fuse (H-08)', () => {
    const state = mdState()
    serializeBlockImage(state, { attrs: { alt: 'alt', src: 'Note.assets/image.png' } })
    const serialized = state.toString() + '### Subtitle'
    expect(serialized).not.toMatch(/\)###/)
    expect(serialized).toContain('![alt](Note.assets/image.png)\n\n### Subtitle')
  })

  it('round-trips image then H2 and H3 through the live editor', () => {
    const src = `# Title\n\n![alt](Note.assets/image.png)\n\n### Subtitle\n`
    const editor = createTestEditor(src)
    const md = editorMarkdown(editor)
    expect(md).not.toMatch(/\)#{1,6}/)
    expect(md).toMatch(/!\[alt]\(Note\.assets\/image\.png\)\n\n### Subtitle/)
    editor.destroy()
  })

  it('H-09 remote image then heading also closes the block', () => {
    const editor = createTestEditor(`![r](https://example.com/a.png)\n\n### After\n`)
    const md = editorMarkdown(editor)
    expect(md).not.toMatch(/\)###/)
    expect(md).toMatch(/### After/)
    editor.destroy()
  })

  it('survives 100 serialize/parse cycles', () => {
    let md = `# Title\n\n![alt](Note.assets/image.png)\n\n## Next\n`
    for (let i = 0; i < 100; i++) {
      const editor = createTestEditor(md)
      md = editorMarkdown(editor)
      editor.destroy()
    }
    expect(md).not.toMatch(/\)##/)
    expect(md).toMatch(/###? Next|## Next/)
  })
})

describe('save-safety validator', () => {
  it('blocks fused image+heading (RT-06)', () => {
    const d = validateMarkdownSafety('![x](a.png)### Hi\n')
    expect(d.some((x) => x.code === 'fused-block-boundary')).toBe(true)
  })

  it('blocks transient URLs (RT-05)', () => {
    expect(validateMarkdownSafety('![x](https://paste.invalid/a)\n').some((x) => x.code === 'transient-url')).toBe(true)
    expect(validateMarkdownSafety('![x](/api/attachments/abc)\n').some((x) => x.code === 'transient-url')).toBe(true)
    expect(validateMarkdownSafety('![x](blob:https://x/1)\n').some((x) => x.code === 'transient-url')).toBe(true)
    expect(validateMarkdownSafety('![x](data:image/png;base64,aa)\n').some((x) => x.code === 'transient-url')).toBe(true)
  })

  it('blocks unbalanced fences (RT-07)', () => {
    expect(validateMarkdownSafety('```powershell\nGet-Date\n').some((x) => x.code === 'unbalanced-fence')).toBe(true)
  })

  it('allows the same strings inside a code fence', () => {
    const md = '```text\n/api/attachments/abc\n```\n'
    expect(validateMarkdownSafety(md).filter((x) => x.code === 'transient-url')).toHaveLength(0)
  })
})

describe('headings', () => {
  it('H-01 caret in paragraph becomes H3', () => {
    const editor = createTestEditor('Hello world')
    const { from } = findTextRange(editor, 'Hello')
    editor.commands.setTextSelection(from + 1)
    applyHeadingToSelection(editor, 3)
    const md = editorMarkdown(editor)
    expect(md).toMatch(/^### Hello world/m)
    const again = createTestEditor(md)
    expect(again.state.doc.firstChild?.type.name).toBe('heading')
    expect(again.state.doc.firstChild?.attrs.level).toBe(3)
    again.destroy()
    editor.destroy()
  })

  it('H-02 full paragraph selected becomes H3 with no extra paragraph', () => {
    const editor = createTestEditor('Hello world')
    editor.commands.selectAll()
    applyHeadingToSelection(editor, 3)
    expect(meaningfulBlocks(editor)).toEqual([{ type: 'heading', text: 'Hello world' }])
    const md = editorMarkdown(editor)
    expect(md).not.toMatch(/\n\n\n/)
    editor.destroy()
  })

  it('H-03 partial selection splits into paragraph/heading/paragraph', () => {
    const editor = createTestEditor('Alpha beta gamma delta')
    const mid = findTextRange(editor, 'beta gamma')
    editor.commands.setTextSelection(mid)
    applyHeadingToSelection(editor, 3)
    expect(topTypes(editor)).toEqual(['paragraph', 'heading', 'paragraph'])
    const texts = topTexts(editor)
    expect(texts[0]).toBe('Alpha ')
    expect(texts[1]).toBe('beta gamma')
    expect(texts[2]).toBe(' delta')
    expect(pmUndo(editor.state, editor.view.dispatch)).toBe(true)
    expect(editor.state.doc.childCount).toBe(1)
    expect(pmRedo(editor.state, editor.view.dispatch)).toBe(true)
    expect(topTypes(editor)).toEqual(['paragraph', 'heading', 'paragraph'])
    editor.destroy()
  })

  it('H-04 preserves whitespace at split boundaries', () => {
    const editor = createTestEditor('')
    editor.commands.setContent({
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [{ type: 'text', text: 'Alpha  beta  gamma' }],
        },
      ],
    })
    const mid = findTextRange(editor, 'beta')
    editor.commands.setTextSelection(mid)
    applyHeadingToSelection(editor, 3)
    const texts = topTexts(editor)
    expect(texts[0]).toBe('Alpha  ')
    expect(texts[1]).toBe('beta')
    editor.destroy()
  })

  it('H-05 partial bold text keeps marks on the heading', () => {
    const editor = createTestEditor('Hello **bold** world')
    const mid = findTextRange(editor, 'bold')
    editor.commands.setTextSelection(mid)
    applyHeadingToSelection(editor, 3)
    expect(topTypes(editor)).toContain('heading')
    const heading = editor.state.doc.child(1)
    expect(heading.type.name).toBe('heading')
    let sawBold = false
    heading.descendants((node) => {
      if (node.isText && node.marks.some((m) => m.type.name === 'bold')) sawBold = true
    })
    expect(sawBold).toBe(true)
    editor.destroy()
  })

  it('H-06 multi-paragraph selection converts eligible blocks only', () => {
    const editor = createTestEditor('First paragraph\n\nSecond paragraph')
    editor.commands.selectAll()
    applyHeadingToSelection(editor, 3)
    const blocks = meaningfulBlocks(editor)
    expect(blocks).toHaveLength(2)
    expect(blocks.every((b) => b.type === 'heading')).toBe(true)
    editor.destroy()
  })

  it('H-07 triple-click overhang does not change the following block', () => {
    const editor = createTestEditor('First paragraph\n\nSecond paragraph')
    const first = findTextRange(editor, 'First paragraph')
    const second = findTextRange(editor, 'Second paragraph')
    editor.commands.setTextSelection({ from: first.from, to: second.from })
    applyHeadingToSelection(editor, 3)
    expect(editor.state.doc.lastChild?.type.name).toBe('paragraph')
    expect(editor.state.doc.lastChild?.textContent).toBe('Second paragraph')
    editor.destroy()
  })

  it('H-08 H3 immediately after local image survives reopen', () => {
    const editor = createTestEditor('![alt](Note.assets/image.png)\n\nSubtitle')
    const t = findTextRange(editor, 'Subtitle')
    editor.commands.setTextSelection(t.from)
    applyHeadingToSelection(editor, 3)
    const md = editorMarkdown(editor)
    expect(md).not.toMatch(/\)###/)
    const again = createTestEditor(md)
    expect(again.state.doc.lastChild?.type.name).toBe('heading')
    expect(again.state.doc.lastChild?.textContent).not.toMatch(/^###/)
    again.destroy()
    editor.destroy()
  })

  it('H-10 heading immediately before image survives', () => {
    const src = `### Before\n\n![alt](Note.assets/image.png)\n`
    const editor = createTestEditor(src)
    const md = editorMarkdown(editor)
    expect(md).toMatch(/### Before/)
    expect(md).toMatch(/!\[alt]/)
    editor.destroy()
  })

  it('H-11 undo restores a single paragraph', () => {
    const editor = createTestEditor('Alpha beta gamma delta')
    editor.commands.setTextSelection(findTextRange(editor, 'beta gamma'))
    applyHeadingToSelection(editor, 3)
    expect(pmUndo(editor.state, editor.view.dispatch)).toBe(true)
    expect(editor.state.doc.childCount).toBe(1)
    expect(editor.state.doc.textContent).toBe('Alpha beta gamma delta')
    editor.destroy()
  })

  it('H-12 redo restores the split heading structure', () => {
    const editor = createTestEditor('Alpha beta gamma delta')
    editor.commands.setTextSelection(findTextRange(editor, 'beta gamma'))
    applyHeadingToSelection(editor, 3)
    expect(pmUndo(editor.state, editor.view.dispatch)).toBe(true)
    expect(pmRedo(editor.state, editor.view.dispatch)).toBe(true)
    expect(topTypes(editor)).toEqual(['paragraph', 'heading', 'paragraph'])
    editor.destroy()
  })

  it('refuses partial heading split inside a list', () => {
    const editor = createTestEditor('- Alpha beta gamma')
    const mid = findTextRange(editor, 'beta')
    editor.commands.setTextSelection(mid)
    const result = applyHeadingToSelection(editor, 3)
    expect(result.ok).toBe(false)
    expect(result.reason).toMatch(/listItem|Partial headings/i)
    editor.destroy()
  })
})

describe('code clipboard', () => {
  it('CODE-01 multiline paste stays in one code block', () => {
    const editor = createTestEditor('```powershell\nGet-Date\n```')
    editor.commands.setTextSelection(editor.state.doc.content.size - 2)
    pastePlainIntoCodeBlock(editor, 'line1\nline2\nline3')
    expect(meaningfulBlocks(editor).filter((b) => b.type === 'codeBlock')).toHaveLength(1)
    expect(editor.state.doc.firstChild?.type.name).toBe('codeBlock')
    expect(editor.state.doc.textContent).toContain('line1\nline2\nline3')
    editor.destroy()
  })

  it('CODE-02 HTML/XML stays literal inside a code block', () => {
    const editor = createTestEditor('```html\n')
    editor.commands.setTextSelection(editor.state.doc.content.size - 2)
    pastePlainIntoCodeBlock(editor, '<div class="x">hi</div>')
    expect(editor.state.doc.firstChild?.type.name).toBe('codeBlock')
    expect(editor.state.doc.textContent).toContain('<div class="x">hi</div>')
    editor.destroy()
  })

  it('CODE-03 preserves tabs', () => {
    expect(clipboardToPlainCode('a\tb\t\n', '')).toBe('a\tb\t\n')
  })

  it('CODE-04/05 preserves leading blanks and whitespace-only', () => {
    expect(clipboardToPlainCode('\n\nfoo\n', '')).toBe('\n\nfoo\n')
    expect(clipboardToPlainCode('   \n', '')).toBe('   \n')
  })

  it('CODE-06 paste backticks stay in the block', () => {
    const editor = createTestEditor('```text\nx\n```')
    editor.commands.setTextSelection(editor.state.doc.content.size - 2)
    pastePlainIntoCodeBlock(editor, '```\ninner\n```')
    expect(editor.state.doc.firstChild?.type.name).toBe('codeBlock')
    expect(editor.state.doc.textContent).toContain('```')
    editor.destroy()
  })

  it('CODE-07 normalizes CRLF only', () => {
    expect(clipboardToPlainCode('a\r\nb\r\n', '')).toBe('a\nb\n')
  })

  it('CODE-08 paste as code is one block and does not trim', () => {
    const editor = createTestEditor('hello')
    editor.commands.selectAll()
    pasteAsCodeBlock(editor, '\nGet-Date\n', 'powershell')
    expect(editor.state.doc.firstChild?.type.name).toBe('codeBlock')
    expect(editor.state.doc.firstChild?.textContent).toBe('\nGet-Date\n')
    editor.destroy()
  })

  it('CODE-09 StartFragment wrapper is stripped without trimming payload blanks', () => {
    const html = '<!--StartFragment--><span>\nfoo\n</span><!--EndFragment-->'
    expect(clipboardToPlainCode('', html)).toBe('\nfoo\n')
  })
})

describe('reload policy', () => {
  it('IMG-07 metadata-only updates do not replace the document', () => {
    const plan = planEditorReload({
      epochChanged: false,
      markdownEqualsLastEmitted: true,
      attachmentsChanged: true,
    })
    expect(plan.replaceDocument).toBe(false)
    expect(plan.updateResolver).toBe(true)
  })

  it('IMG-07 attachment inventory does not change the document or undo stack', () => {
    const editor = createTestEditor('Hello world')
    editor.commands.setTextSelection(2)
    const before = editor.state.doc.toJSON()
    const from = editor.state.selection.from
    dispatchAttachmentInventory(editor, [{ id: 'a1', fileName: 'x.png', contentType: 'image/png' }])
    expect(editor.state.doc.toJSON()).toEqual(before)
    expect(editor.state.selection.from).toBe(from)
    editor.destroy()
  })
})

describe('paste sessions', () => {
  it('IMG-02 placeholder stays at the original position after typing elsewhere', async () => {
    const editor = createTestEditor('AAA\n\nBBB')
    const startSize = editor.state.doc.content.size
    editor.commands.setTextSelection(1)
    insertPendingAssetAtSelection(editor, {
      uploadId: 'u1',
      pasteSessionId: 's1',
      alt: 'shot',
      status: 'uploading',
    })
    editor.commands.setTextSelection(editor.state.doc.content.size)
    editor.commands.insertContent(' typed')
    await runPasteSession(
      editor,
      {
        noteId: 'n',
        noteSessionId: 'sess',
        uploadFile: async () => ({
          success: true,
          markdownPath: 'n.assets/shot.png',
          fileName: 'shot.png',
        }),
        importRemote: async () => ({ success: false }),
        getNoteSessionId: () => 'sess',
      },
      [{ uploadId: 'u1', kind: 'file', file: new File([new Uint8Array([1])], 'shot.png', { type: 'image/png' }) }],
    )
    expect(pendingCount(editor)).toBe(0)
    expect(imageSrcs(editor)[0]).toBe('n.assets/shot.png')
    expect(editor.state.doc.firstChild?.type.name).toBe('image')
    expect(editor.state.doc.textContent).toContain('typed')
    expect(editor.state.doc.content.size).toBeGreaterThan(startSize)
    editor.destroy()
  })

  it('IMG-03 reverse completion keeps original placeholder order', async () => {
    const editor = createTestEditor('start')
    editor.commands.setTextSelection(editor.state.doc.content.size)
    insertPendingAssetAtSelection(editor, { uploadId: 'first', pasteSessionId: 's', status: 'uploading' })
    editor.commands.setTextSelection(editor.state.doc.content.size)
    insertPendingAssetAtSelection(editor, { uploadId: 'second', pasteSessionId: 's', status: 'uploading' })
    await runPasteSession(
      editor,
      {
        noteId: 'n',
        noteSessionId: 'sess',
        uploadFile: async (_id, file) => {
          const delay = file.name === 'a.png' ? 40 : 5
          await new Promise((r) => setTimeout(r, delay))
          return { success: true, markdownPath: `n.assets/${file.name}`, fileName: file.name }
        },
        importRemote: async () => ({ success: false }),
        getNoteSessionId: () => 'sess',
      },
      [
        { uploadId: 'first', kind: 'file', file: new File([new Uint8Array([1])], 'a.png', { type: 'image/png' }) },
        { uploadId: 'second', kind: 'file', file: new File([new Uint8Array([1])], 'b.png', { type: 'image/png' }) },
      ],
    )
    expect(imageSrcs(editor)).toEqual(['n.assets/a.png', 'n.assets/b.png'])
    editor.destroy()
  })

  it('IMG-06 localizes only the new placeholder, not an existing remote image', async () => {
    const editor = createTestEditor('![old](https://example.com/same.png)\n\nkeep')
    editor.commands.setTextSelection(editor.state.doc.content.size)
    insertPendingAssetAtSelection(editor, { uploadId: 'new', pasteSessionId: 's', status: 'uploading' })
    await runPasteSession(
      editor,
      {
        noteId: 'n',
        noteSessionId: 'sess',
        uploadFile: async () => ({ success: false }),
        importRemote: async () => ({
          success: true,
          markdownPath: 'n.assets/new.png',
          fileName: 'new.png',
        }),
        getNoteSessionId: () => 'sess',
      },
      [{ uploadId: 'new', kind: 'remote', remoteUrl: 'https://example.com/same.png' }],
    )
    expect(imageSrcs(editor)).toEqual(['https://example.com/same.png', 'n.assets/new.png'])
    editor.destroy()
  })

  it('IMG-08 failed upload leaves a failed placeholder and blocks serialize', async () => {
    const editor = createTestEditor('hi')
    insertPendingAssetAtSelection(editor, { uploadId: 'bad', pasteSessionId: 's', status: 'uploading' })
    const { failed } = await runPasteSession(
      editor,
      {
        noteId: 'n',
        noteSessionId: 'sess',
        uploadFile: async () => ({ success: false, error: 'nope' }),
        importRemote: async () => ({ success: false }),
        getNoteSessionId: () => 'sess',
      },
      [{ uploadId: 'bad', kind: 'file', file: new File([new Uint8Array([1])], 'x.png', { type: 'image/png' }) }],
    )
    expect(failed).toBe(1)
    expect(pendingCount(editor)).toBe(1)
    expect(() => editorMarkdown(editor)).toThrow(/pending/)
    editor.destroy()
  })

  it('IMG-10 late results are ignored after the note session changes', async () => {
    const editor = createTestEditor('hi')
    insertPendingAssetAtSelection(editor, { uploadId: 'late', pasteSessionId: 's', status: 'uploading' })
    let session = 'old'
    await runPasteSession(
      editor,
      {
        noteId: 'n',
        noteSessionId: 'old',
        uploadFile: async () => {
          session = 'new'
          await new Promise((r) => setTimeout(r, 20))
          return { success: true, markdownPath: 'n.assets/x.png', fileName: 'x.png' }
        },
        importRemote: async () => ({ success: false }),
        getNoteSessionId: () => session,
      },
      [{ uploadId: 'late', kind: 'file', file: new File([new Uint8Array([1])], 'x.png', { type: 'image/png' }) }],
    )
    expect(imageSrcs(editor)).toHaveLength(0)
    expect(pendingCount(editor)).toBe(1)
    editor.destroy()
  })

  it('IMG-04 rewritePastedImagesToPlaceholders strips data: URLs from HTML', () => {
    const { html, jobs } = rewritePastedImagesToPlaceholders(
      '<p>hi</p><img src="data:image/png;base64,aa==" alt="x">',
      'sess',
    )
    expect(html).not.toContain('data:image')
    expect(html).toContain('data-pending-asset')
    expect(jobs).toHaveLength(1)
    expect(jobs[0]?.kind).toBe('data')
  })
})

describe('image adjacency round-trip', () => {
  it('IMG-12 image between H2 and H3', () => {
    const md = `## Top\n\n![x](n.assets/a.png)\n\n### Bottom\n`
    const editor = createTestEditor(md)
    const out = editorMarkdown(editor)
    expect(out).not.toMatch(/\)#/)
    const again = createTestEditor(out)
    const types = topTypes(again)
    expect(types).toContain('heading')
    expect(types).toContain('image')
    again.destroy()
    editor.destroy()
  })

  it('IMG-13 image after code block keeps a balanced fence', () => {
    const md = '```powershell\nGet-Date\n```\n\n![x](n.assets/a.png)\n'
    const editor = createTestEditor(md)
    const out = editorMarkdown(editor)
    expect((out.match(/^```/gm) ?? []).length % 2).toBe(0)
    expect(out).toMatch(/!\[x]/)
    editor.destroy()
  })

  it('IMG-14 image after a list', () => {
    const md = '- one\n- two\n\n![x](n.assets/a.png)\n'
    const editor = createTestEditor(md)
    const out = editorMarkdown(editor)
    expect(out).not.toMatch(/\)-/)
    expect(out).toMatch(/!\[x]/)
    editor.destroy()
  })
})

describe('locality sentinels', () => {
  it('LOC-01 fingerprints around an image stay stable after serialize', () => {
    const md = `S1\n\nS2\n\nS3\n\nS4\n\nS5\n\n![x](n.assets/a.png)\n\nA1\n\nA2\n\nA3\n\nA4\n\nA5\n`
    const a = createTestEditor(md)
    const before = blockFingerprints(a.state.doc)
    const round = editorMarkdown(a)
    a.destroy()
    const b = createTestEditor(round)
    const after = blockFingerprints(b.state.doc)
    expect(after.slice(0, 5)).toEqual(before.slice(0, 5))
    expect(after.slice(-5)).toEqual(before.slice(-5))
    b.destroy()
  })

  it('LOC-02/03 paste resolve does not change sentinel fingerprints', async () => {
    const md = `S1\n\nS2\n\nS3\n\nS4\n\nS5\n\nmiddle\n\nA1\n\nA2\n\nA3\n\nA4\n\nA5\n`
    const editor = createTestEditor(md)
    const before = blockFingerprints(editor.state.doc)
    const mid = findTextRange(editor, 'middle')
    editor.commands.setTextSelection(mid.to)
    insertPendingAssetAtSelection(editor, { uploadId: 'p', pasteSessionId: 's', status: 'uploading' })
    await runPasteSession(
      editor,
      {
        noteId: 'n',
        noteSessionId: 'sess',
        uploadFile: async () => ({ success: true, markdownPath: 'n.assets/p.png', fileName: 'p.png' }),
        importRemote: async () => ({ success: false }),
        getNoteSessionId: () => 'sess',
      },
      [{ uploadId: 'p', kind: 'file', file: new File([new Uint8Array([1])], 'p.png', { type: 'image/png' }) }],
    )
    const after = blockFingerprints(editor.state.doc)
    expect(after.slice(0, 5)).toEqual(before.slice(0, 5))
    expect(after.slice(-5)).toEqual(before.slice(-5))
    editor.destroy()
  })
})

describe('revision coordinator', () => {
  it('SAVE-08 does not emit markdown while a placeholder is pending', () => {
    const editor = createTestEditor('hi')
    const emitted: string[] = []
    const coord = new EditorRevisionCoordinator({
      debounceMs: 1,
      onValidatedChange: (p) => emitted.push(p.markdown),
    })
    coord.attach(editor)
    insertPendingAssetAtSelection(editor, { uploadId: 'p', pasteSessionId: 's', status: 'uploading' })
    coord.observeTransaction(editor.state.tr)
    expect(coord.flush()).toBe(false)
    expect(emitted).toHaveLength(0)
    editor.destroy()
  })
})

describe('document sameness vectors', () => {
  it('matches shared JSON vectors', () => {
    const path = resolve(repoRoot, 'tests/shared/document-sameness-vectors.json')
    const data = JSON.parse(readFileSync(path, 'utf8')) as {
      vectors: { id: string; a: string; b: string; exactSaveEqual: boolean }[]
    }
    for (const v of data.vectors) {
      expect(exactSaveEqual(v.a, v.b), v.id).toBe(v.exactSaveEqual)
    }
  })
})

describe('torture fixture', () => {
  it('RT-01 editor-boundary-torture serializes without fused image/heading', () => {
    const path = resolve(repoRoot, 'tests/RoundTripFixtures/editor-boundary-torture/Editor Boundary Torture.md')
    const raw = readFileSync(path, 'utf8')
    const body = raw.replace(/^---[\s\S]*?---\s*/, '')
    const editor = createTestEditor(body)
    const dump = dumpEditor(editor)
    expect(dump.markdown).not.toMatch(/\)#{1,6}/)
    editor.destroy()
  })
})

describe('stress 50x', () => {
  it('H-08 image+heading round-trip', () => {
    for (let i = 0; i < 50; i++) {
      const editor = createTestEditor('![alt](Note.assets/image.png)\n\n### Subtitle')
      const md = editorMarkdown(editor)
      expect(md).not.toMatch(/\)###/)
      editor.destroy()
    }
  })

  it('IMG-02 placeholder locality', async () => {
    for (let i = 0; i < 50; i++) {
      const editor = createTestEditor('AAA')
      insertPendingAssetAtSelection(editor, { uploadId: `u${i}`, pasteSessionId: 's', status: 'uploading' })
      editor.commands.insertContent('Z')
      await runPasteSession(
        editor,
        {
          noteId: 'n',
          noteSessionId: 'sess',
          uploadFile: async () => ({ success: true, markdownPath: 'n.assets/x.png', fileName: 'x.png' }),
          importRemote: async () => ({ success: false }),
          getNoteSessionId: () => 'sess',
        },
        [{ uploadId: `u${i}`, kind: 'file', file: new File([new Uint8Array([1])], 'x.png', { type: 'image/png' }) }],
      )
      expect(imageSrcs(editor)[0]).toBe('n.assets/x.png')
      editor.destroy()
    }
  })

  it('IMG-03 reverse order', async () => {
    for (let i = 0; i < 50; i++) {
      const editor = createTestEditor('x')
      insertPendingAssetAtSelection(editor, { uploadId: `a${i}`, pasteSessionId: 's', status: 'uploading' })
      editor.commands.setTextSelection(editor.state.doc.content.size)
      insertPendingAssetAtSelection(editor, { uploadId: `b${i}`, pasteSessionId: 's', status: 'uploading' })
      await runPasteSession(
        editor,
        {
          noteId: 'n',
          noteSessionId: 'sess',
          uploadFile: async (_id, file) => {
            await new Promise((r) => setTimeout(r, file.name === 'a.png' ? 8 : 1))
            return { success: true, markdownPath: `n.assets/${file.name}`, fileName: file.name }
          },
          importRemote: async () => ({ success: false }),
          getNoteSessionId: () => 'sess',
        },
        [
          { uploadId: `a${i}`, kind: 'file', file: new File([new Uint8Array([1])], 'a.png', { type: 'image/png' }) },
          { uploadId: `b${i}`, kind: 'file', file: new File([new Uint8Array([1])], 'b.png', { type: 'image/png' }) },
        ],
      )
      expect(imageSrcs(editor)).toEqual(['n.assets/a.png', 'n.assets/b.png'])
      editor.destroy()
    }
  })

  it('CODE-01 multiline stays in the block', () => {
    for (let i = 0; i < 50; i++) {
      const editor = createTestEditor('```text\nbase\n```')
      editor.commands.setTextSelection(editor.state.doc.content.size - 2)
      pastePlainIntoCodeBlock(editor, 'a\nb\nc')
      expect(editor.state.doc.firstChild?.type.name).toBe('codeBlock')
      editor.destroy()
    }
  })

  it('LOC-01 sentinel fingerprints', () => {
    for (let i = 0; i < 50; i++) {
      const md = `S1\n\nS2\n\nS3\n\nS4\n\nS5\n\n![x](n.assets/a.png)\n\nA1\n\nA2\n\nA3\n\nA4\n\nA5\n`
      const a = createTestEditor(md)
      const before = blockFingerprints(a.state.doc)
      const round = editorMarkdown(a)
      a.destroy()
      const b = createTestEditor(round)
      expect(blockFingerprints(b.state.doc).slice(0, 5)).toEqual(before.slice(0, 5))
      b.destroy()
    }
  })
})
