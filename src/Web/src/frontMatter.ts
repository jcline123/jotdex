/** Split YAML front matter from Markdown body for safe rich editing. */
export function splitFrontMatter(markdown: string): { frontMatter: string; body: string } {
  if (!markdown.startsWith('---')) {
    return { frontMatter: '', body: markdown }
  }
  const end = markdown.indexOf('\n---', 3)
  if (end < 0) {
    return { frontMatter: '', body: markdown }
  }
  const frontMatter = markdown.slice(0, end + 4) // include closing ---
  let body = markdown.slice(end + 4)
  if (body.startsWith('\r\n')) body = body.slice(2)
  else if (body.startsWith('\n')) body = body.slice(1)
  return { frontMatter, body }
}

export function joinFrontMatter(frontMatter: string, body: string): string {
  if (!frontMatter) return body
  const b = body.replace(/^\r?\n/, '')
  return `${frontMatter}\n\n${b}`
}

/** Set or clear `favorite: true` in YAML front matter (creates a minimal FM block if needed). */
export function setFavoriteInMarkdown(markdown: string, favorite: boolean): string {
  const { frontMatter, body } = splitFrontMatter(markdown)
  if (!frontMatter) {
    if (!favorite) return markdown
    return `---\nfavorite: true\n---\n\n${markdown.replace(/^\r?\n/, '')}`
  }
  const lines = frontMatter.split(/\r?\n/)
  const out: string[] = []
  let saw = false
  for (const line of lines) {
    if (/^favorite\s*:/i.test(line) || /^favourite\s*:/i.test(line)) {
      saw = true
      if (favorite) out.push('favorite: true')
      continue
    }
    out.push(line)
  }
  if (favorite && !saw) {
    // insert before closing ---
    const close = out.findIndex((l, i) => i > 0 && l.trim() === '---')
    if (close > 0) out.splice(close, 0, 'favorite: true')
    else out.push('favorite: true')
  }
  return joinFrontMatter(out.join('\n'), body)
}

export function isFavoriteFrontMatter(markdown: string): boolean {
  const { frontMatter } = splitFrontMatter(markdown)
  return /^favorite\s*:\s*(true|yes|1)\s*$/im.test(frontMatter) ||
    /^favourite\s*:\s*(true|yes|1)\s*$/im.test(frontMatter)
}

/** Compare docs ignoring TipTap/Markdown cosmetic differences (newlines, trailing space). */
export function normalizeMarkdown(markdown: string): string {
  return markdown
    .replace(/\r\n/g, '\n')
    .replace(/[ \t]+\n/g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .replace(/^\n+/, '')
    .replace(/\n+$/, '\n')
}

export function sameMarkdown(a: string, b: string): boolean {
  return normalizeMarkdown(a) === normalizeMarkdown(b)
}
