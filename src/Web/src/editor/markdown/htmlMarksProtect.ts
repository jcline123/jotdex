/** Rewrite inline HTML marks outside fences into brace tokens the official lexer will not split on `>`. */
const MARKS: { tag: string; token: string }[] = [
  { tag: 'u', token: 'jotdex-u' },
  { tag: 'sub', token: 'jotdex-sub' },
  { tag: 'sup', token: 'jotdex-sup' },
  { tag: 'mark', token: 'jotdex-mark' },
]

export function rewriteHtmlMarksToBraces(markdown: string): { markdown: string; changed: boolean } {
  let changed = false
  let inFence = false
  const out = markdown.split('\n').map((line) => {
    if (line.trimStart().startsWith('```')) {
      inFence = !inFence
      return line
    }
    if (inFence) return line
    let next = line
    for (const { tag, token } of MARKS) {
      const re = new RegExp(`<${tag}>([\\s\\S]*?)</${tag}>`, 'gi')
      next = next.replace(re, (_m, inner: string) => {
        changed = true
        return `{${token}:${encodeURIComponent(inner)}}`
      })
    }
    return next
  })
  return { markdown: out.join('\n'), changed }
}
