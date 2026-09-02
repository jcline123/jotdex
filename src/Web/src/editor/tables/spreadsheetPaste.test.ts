import { describe, expect, it } from 'vitest'
import { createTestEditor, editorMarkdown, findTextRange } from '../testing/createTestEditor'
import { parseSpreadsheet, pasteSpreadsheetIntoTable, stripTableMerges } from './spreadsheetPaste'

describe('spreadsheet paste', () => {
  it('parses TSV and CSV', () => {
    expect(parseSpreadsheet('a\tb\n1\t2')).toEqual([
      ['a', 'b'],
      ['1', '2'],
    ])
    expect(parseSpreadsheet('a,b\n1,2')).toEqual([
      ['a', 'b'],
      ['1', '2'],
    ])
  })

  it('strips merge attributes from HTML', () => {
    expect(stripTableMerges('<td colspan="2" rowspan="3">x</td>')).toBe('<td>x</td>')
  })

  it('fills the current table from the selected cell and grows as needed', () => {
    const editor = createTestEditor('| A | B |\n| --- | --- |\n| 1 | 2 |\n')
    const range = findTextRange(editor, '1')
    editor.chain().setTextSelection(range).run()
    expect(pasteSpreadsheetIntoTable(editor, [['x', 'y', 'z'], ['p', 'q', 'r']])).toBe(true)
    const md = editorMarkdown(editor)
    expect(md).toContain('x')
    expect(md).toContain('y')
    expect(md).toContain('z')
    expect(md).toContain('p')
    expect(md).toContain('q')
    expect(md).toContain('r')
    expect(md).toContain('A')
    expect(md).toContain('B')
    editor.destroy()
  })
})
