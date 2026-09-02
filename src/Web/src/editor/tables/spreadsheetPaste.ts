import type { Editor } from '@tiptap/core'
import type { Node as PmNode, Schema } from '@tiptap/pm/model'
import { TableMap } from '@tiptap/pm/tables'

export function stripTableMerges(html: string): string {
  return html.replace(/\s(colspan|rowspan)="\d+"/gi, '')
}

export function parseSpreadsheet(plain: string): string[][] | null {
  const text = plain.replace(/\r\n/g, '\n').replace(/\r/g, '\n').trimEnd()
  if (!text.includes('\t') && !/^[^,\n]+,[^,\n]+/m.test(text)) return null
  const lines = text.split('\n').filter((l) => l.length > 0)
  if (lines.length < 1) return null
  const delim = lines[0]!.includes('\t') ? '\t' : ','
  const rows = lines.map((line) => line.split(delim).map((c) => c.trim()))
  const cols = Math.max(...rows.map((r) => r.length))
  if (cols < 2 && rows.length < 2) return null
  return rows.map((r) => {
    const next = r.slice()
    while (next.length < cols) next.push('')
    return next
  })
}

function tableAtSelection(editor: Editor): { tablePos: number; table: PmNode; tableStart: number } | null {
  const { $from } = editor.state.selection
  for (let d = $from.depth; d > 0; d--) {
    const node = $from.node(d)
    if (node.type.spec.tableRole === 'table' || node.type.name === 'table') {
      const tablePos = $from.before(d)
      return { tablePos, table: node, tableStart: tablePos + 1 }
    }
  }
  return null
}

function cellRelPos(editor: Editor, tableStart: number): number | null {
  const { $from } = editor.state.selection
  for (let d = $from.depth; d > 0; d--) {
    const name = $from.node(d).type.name
    if (name === 'tableCell' || name === 'tableHeader') {
      return $from.before(d) - tableStart
    }
  }
  return null
}

function gridFromTable(table: PmNode): string[][] {
  const map = TableMap.get(table)
  const out: string[][] = []
  for (let r = 0; r < map.height; r++) {
    const row: string[] = []
    for (let c = 0; c < map.width; c++) {
      const rel = map.map[r * map.width + c]!
      const cell = table.nodeAt(rel)
      row.push(cell?.textContent ?? '')
    }
    out.push(row)
  }
  return out
}

function headerRowCount(table: PmNode): number {
  let n = 0
  for (let i = 0; i < table.childCount; i++) {
    const row = table.child(i)
    if (row.firstChild?.type.name === 'tableHeader') n++
    else break
  }
  return n
}

function createTableNode(schema: Schema, grid: string[][], headerRows: number): PmNode {
  const paragraph = schema.nodes.paragraph
  const tableRow = schema.nodes.tableRow
  const table = schema.nodes.table
  const header = schema.nodes.tableHeader
  const cell = schema.nodes.tableCell
  if (!paragraph || !tableRow || !table || !header || !cell) {
    throw new Error('table schema missing')
  }
  const rows = grid.map((cells, ri) => {
    const type = ri < headerRows ? header : cell
    const rowCells = cells.map((text) => {
      const para = text ? paragraph.create(null, schema.text(text)) : paragraph.createAndFill()!
      return type.createAndFill(null, para)!
    })
    return tableRow.create(null, rowCells)
  })
  return table.create(null, rows)
}

/** Fill the current table from the selected cell, growing rows/cols as needed. */
export function pasteSpreadsheetIntoTable(editor: Editor, grid: string[][]): boolean {
  if (!grid.length || !(grid[0]?.length ?? 0)) return false
  const found = tableAtSelection(editor)
  if (!found) return false
  const { tablePos, table, tableStart } = found
  const rel = cellRelPos(editor, tableStart)
  if (rel == null) return false
  let startRow = 0
  let startCol = 0
  try {
    const rect = TableMap.get(table).findCell(rel)
    startRow = rect.top
    startCol = rect.left
  } catch {
    return false
  }
  const existing = gridFromTable(table)
  const height = Math.max(existing.length, startRow + grid.length)
  const width = Math.max(existing[0]?.length ?? 0, startCol + (grid[0]?.length ?? 0))
  const merged: string[][] = []
  for (let r = 0; r < height; r++) {
    const row: string[] = []
    for (let c = 0; c < width; c++) {
      const pr = r - startRow
      const pc = c - startCol
      if (pr >= 0 && pc >= 0 && grid[pr] && grid[pr]![pc] !== undefined) {
        row.push(grid[pr]![pc]!)
      } else {
        row.push(existing[r]?.[c] ?? '')
      }
    }
    merged.push(row)
  }
  const next = createTableNode(editor.state.schema, merged, headerRowCount(table))
  editor.view.dispatch(editor.state.tr.replaceWith(tablePos, tablePos + table.nodeSize, next))
  return true
}
