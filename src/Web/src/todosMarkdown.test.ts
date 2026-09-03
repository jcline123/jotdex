import { describe, expect, it } from 'vitest'
import {
  mergeRailTodos,
  parseTodosMarkdown,
  serializeTodosMarkdown,
  sortTodos,
  type TodoItem,
  type VaultTaskLike,
} from './todosMarkdown'

function item(partial: Partial<TodoItem> & Pick<TodoItem, 'id' | 'title'>): TodoItem {
  return {
    priority: 'normal',
    due: null,
    remind: 'off',
    ...partial,
  }
}

describe('todosMarkdown', () => {
  it('parses and serializes added timestamps', () => {
    const md =
      '- [ ] Call vendor <!-- jotdex-todo id="a" priority="high" remind="off" added="2026-09-01T12:00:00.000Z" -->\n'
    const items = parseTodosMarkdown(md)
    expect(items).toHaveLength(1)
    expect(items[0]!.added).toBe('2026-09-01T12:00:00.000Z')
    expect(serializeTodosMarkdown('', items)).toContain('added="2026-09-01T12:00:00.000Z"')
  })

  it('sorts by priority then newest added', () => {
    const sorted = sortTodos([
      item({ id: 'old', title: 'older normal', added: '2026-01-01T00:00:00.000Z' }),
      item({ id: 'new', title: 'newer normal', added: '2026-09-01T00:00:00.000Z' }),
      item({ id: 'hi', title: 'high', priority: 'high', added: '2026-01-01T00:00:00.000Z' }),
    ])
    expect(sorted.map((t) => t.id)).toEqual(['hi', 'new', 'old'])
  })

  it('merges rail and note tasks into one ordered list', () => {
    const local = [item({ id: 'l', title: 'Buy milk', priority: 'normal', added: '2026-09-02T00:00:00.000Z' })]
    const vault: VaultTaskLike[] = [
      {
        id: 'v',
        text: 'File taxes',
        priority: 'high',
        noteId: 'n1',
        noteTitle: 'Finance',
        added: '2026-01-01T00:00:00.000Z',
      },
    ]
    const rows = mergeRailTodos(local, vault)
    expect(rows.map((r) => (r.kind === 'local' ? r.item.id : r.task.id))).toEqual(['v', 'l'])
  })
})
