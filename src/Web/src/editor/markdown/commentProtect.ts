/** Named transform: rewrite HTML comments outside fences into brace tokens the official lexer can keep. */
export function rewriteCommentsToBraces(markdown: string): { markdown: string; changed: boolean } {
  let changed = false
  let inFence = false
  const out = markdown.split('\n').map((line) => {
    if (line.trimStart().startsWith('```')) {
      inFence = !inFence
      return line
    }
    if (inFence) return line
    const next = line.replace(/<!--\s*(jotdex-task|jotdex-todo)\s+([^>]*)-->/g, (_m, kind: string, attrs: string) => {
      changed = true
      return `{${kind} ${attrs.trim()}}`
    }).replace(/<!--([\s\S]*?)-->/g, (_m, inner: string) => {
      changed = true
      return `{html-comment:${encodeURIComponent(inner)}}`
    })
    return next
  })
  return { markdown: out.join('\n'), changed }
}
