import { encodeStyleBrace, parseStyleAttrs } from '../extensions/JotdexTextStyleMarkdown'

/** Rewrite inline HTML marks outside fences into brace tokens the official lexer will not split on `>`. */
const MARKS: { tag: string; token: string }[] = [
  { tag: 'u', token: 'jotdex-u' },
  { tag: 'sub', token: 'jotdex-sub' },
  { tag: 'sup', token: 'jotdex-sup' },
  { tag: 'mark', token: 'jotdex-mark' },
]

const STYLE_SPAN_RE = /<span\s+style\s*=\s*(["'])([\s\S]*?)\1\s*>([\s\S]*?)<\/span>/gi

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
    // Color/size spans include `#` and `>` in the opening tag; Marked splits those into literal text.
    next = next.replace(STYLE_SPAN_RE, (_m, _q: string, style: string, inner: string) => {
      const { color, fontSize } = parseStyleAttrs(style)
      if (!color && !fontSize) return _m
      changed = true
      return encodeStyleBrace(color, fontSize, inner)
    })
    return next
  })
  return { markdown: out.join('\n'), changed }
}
