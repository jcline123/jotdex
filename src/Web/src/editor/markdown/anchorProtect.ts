/** Rewrite empty `<a id="…"></a>` so Marked does not split on the tag's `>`. */
const EMPTY_ANCHOR_RE = /<a\s+([^>]*?\bid\s*=\s*(["'])([^"']+)\2[^>]*?)>\s*<\/a>/gi

export function rewriteAnchorsToBraces(markdown: string): { markdown: string; changed: boolean } {
  let changed = false
  let inFence = false
  const out = markdown.split('\n').map((line) => {
    if (line.trimStart().startsWith('```')) {
      inFence = !inFence
      return line
    }
    if (inFence) return line
    return line.replace(EMPTY_ANCHOR_RE, (_m, _attrs: string, _q: string, id: string) => {
      changed = true
      return `{jotdex-anchor:${encodeURIComponent(id)}}`
    })
  })
  return { markdown: out.join('\n'), changed }
}
