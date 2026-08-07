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
