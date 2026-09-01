/**
 * Exact save equivalence — shared with C# NoteCommandService.NormalizeDoc.
 * Line endings to LF, trim end, ignore YAML `modified:`. Do not collapse interior blanks.
 */
export function exactSaveNormalize(content: string): string {
  let n = content.replace(/\r\n/g, '\n').replace(/\r/g, '\n').trimEnd()
  if (n.startsWith('---')) {
    const end = n.indexOf('\n---', 3)
    if (end > 0) {
      const header = n.slice(3, end).replace(/^modified:\s*.*$/gim, 'modified:')
      n = '---' + header + n.slice(end)
    }
  }
  return n
}

export function exactSaveEqual(a: string, b: string): boolean {
  return exactSaveNormalize(a) === exactSaveNormalize(b)
}
