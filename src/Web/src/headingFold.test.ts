import { describe, expect, it } from 'vitest'
import {
  headingFoldKey,
  headingIdentities,
  keysFromFoldedPositions,
  positionsFromFoldKeys,
  restoreHeadingFolds,
  persistHeadingFolds,
  peekLegacyBrowserFoldKeys,
  clearLegacyBrowserFoldKeys,
  foldsForNote,
  unfoldHeadingsContaining,
} from './headingFold'
import { createTestEditor } from './editor/testing/createTestEditor'

describe('headingFold persistence', () => {
  it('identifies duplicate headings by occurrence', () => {
    const editor = createTestEditor('# Alpha\n\ntext\n\n## Beta\n\n# Alpha\n')
    const ids = headingIdentities(editor.state.doc)
    expect(ids.map((h) => h.key)).toEqual(['1:1:Alpha', '2:1:Beta', '1:2:Alpha'])
    editor.destroy()
  })

  it('round-trips fold keys to positions', () => {
    const editor = createTestEditor('# Keep\n\nbody\n\n## Hide me\n\nhidden\n')
    const ids = headingIdentities(editor.state.doc)
    const hide = ids.find((h) => h.key.includes('Hide me'))
    expect(hide).toBeTruthy()
    const keys = keysFromFoldedPositions(editor.state.doc, new Set([hide!.pos]))
    const back = positionsFromFoldKeys(editor.state.doc, keys)
    expect([...back]).toEqual([hide!.pos])
    editor.destroy()
  })

  it('prefers vault keys over leftover browser storage', () => {
    localStorage.setItem('jotdex.headingFolds', JSON.stringify({ 'note-a': ['1:1:Old'] }))
    expect(foldsForNote('note-a', ['1:1:Vault']).keys).toEqual(['1:1:Vault'])
    expect(peekLegacyBrowserFoldKeys('note-a')).toEqual([])
  })

  it('migrates browser storage when the vault helper is empty', () => {
    localStorage.setItem('jotdex.headingFolds', JSON.stringify({ 'note-b': ['2:1:Later'] }))
    const got = foldsForNote('note-b', [])
    expect(got.keys).toEqual(['2:1:Later'])
    expect(got.migrateFromBrowser).toBe(true)
    clearLegacyBrowserFoldKeys('note-b')
    expect(peekLegacyBrowserFoldKeys('note-b')).toEqual([])
  })

  it('restores folds from key list after a document load', () => {
    const editor = createTestEditor('# Hide\n\nsecret\n\n# Keep\n\nvisible\n')
    const hide = headingIdentities(editor.state.doc).find((h) => h.key.includes('Hide'))
    expect(hide).toBeTruthy()
    restoreHeadingFolds(editor, ['1:1:Hide'])
    const state = headingFoldKey.getState(editor.state) as { folded: Set<number> }
    expect(state.folded.has(hide!.pos)).toBe(true)
    editor.destroy()
  })

  it('outline jump unfolds a folded ancestor without changing saved keys', () => {
    const editor = createTestEditor('# Parent\n\n## Child\n\nbody\n')
    const ids = headingIdentities(editor.state.doc)
    const parent = ids.find((h) => h.key.includes('Parent'))
    const child = ids.find((h) => h.key.includes('Child'))
    expect(parent && child).toBeTruthy()
    restoreHeadingFolds(editor, ['1:1:Parent'])
    expect((headingFoldKey.getState(editor.state) as { folded: Set<number> }).folded.has(parent!.pos)).toBe(true)
    unfoldHeadingsContaining(editor, child!.pos + 1)
    expect((headingFoldKey.getState(editor.state) as { folded: Set<number> }).folded.has(parent!.pos)).toBe(false)
    expect(persistHeadingFolds(editor)).toEqual(['1:1:Parent'])
    editor.destroy()
  })
})
