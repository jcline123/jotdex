import { describe, expect, it } from 'vitest'
import { createEditorCommandRegistry } from './createEditorCommandRegistry'
import { createTestEditor } from '../testing/createTestEditor'
import { findTextRange } from '../testing/createTestEditor'

describe('CMD command registry', () => {
  it('lists stable ids used by toolbar and slash', () => {
    const registry = createEditorCommandRegistry()
    expect(registry.get('mark.bold')?.label).toBe('Bold')
    expect(registry.slashItems('').some((c) => c.id === 'heading.1')).toBe(true)
    expect(registry.plusItems('table').some((c) => c.id === 'block.table')).toBe(true)
  })

  it('CMD-bold toggles bold on a selection', () => {
    const editor = createTestEditor('Hello world')
    const range = findTextRange(editor, 'Hello')
    editor.chain().setTextSelection(range).run()
    const registry = createEditorCommandRegistry()
    const result = registry.execute('mark.bold', { editor })
    expect(result.ok).toBe(true)
    expect(editor.isActive('bold')).toBe(true)
    editor.destroy()
  })
})
